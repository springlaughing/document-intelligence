using DocumentService.Api.Infrastructure.Ef;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using DotNet.Testcontainers.Networks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using Testcontainers.ServiceBus;

namespace DocumentIntelligence.IntegrationTests;

// Starts a Service Bus emulator, the SQL Server it depends on, and the analysis worker, for
// the lifetime of the test collection.
//
// The containers are started by the test run rather than found already running, which is
// what makes these tests reproducible and what lets them work in CI, where nothing is
// running beforehand. It also means the entity config is ours: the emulator only creates
// queues, topics and subscriptions from its config file at startup and has no management
// API, so a test cannot create a subscription for itself at runtime. Declaring one
// subscription per test in servicebus-test-config.json is the only way to stop tests
// consuming each other's messages.
//
// The worker runs as its real image, built from its own Dockerfile. That is deliberate: an
// in-process copy of its wiring would test classes this repo already covers, while leaving
// its composition root - the part that has actually broken before - unexercised.
public sealed class SharedContainerFixture : IAsyncLifetime
{
    // Images are named here rather than left to the library's defaults. Testcontainers
    // now wants the image passed to the builder, and its default SQL tag is not the one
    // usually already pulled locally - pinning avoids a second multi-gigabyte download.
    private const string SqlImage = "mcr.microsoft.com/mssql/server:2022-latest";
    private const string ServiceBusImage = "mcr.microsoft.com/azure-messaging/servicebus-emulator:latest";

    // The worker reaches the broker by this name on the shared network, so it has to match
    // the host in the connection string handed to the worker container below.
    private const string ServiceBusAlias = "servicebus";

    private INetwork? _network;
    private MsSqlContainer? _sql;
    private ServiceBusContainer? _serviceBus;
    private IFutureDockerImage? _workerImage;
    private IContainer? _worker;

    public string ServiceBusConnectionString { get; private set; } = "";

    /// Points at master; use CreateDatabaseAsync for something a test can own.
    public string SqlConnectionString { get; private set; } = "";

    public bool Started { get; private set; }

    /// Why the fixture did not start, for a skip message that says something useful.
    public string? StartupFailure { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            _network = new NetworkBuilder().Build();

            _sql = new MsSqlBuilder(SqlImage)
                .WithNetwork(_network)
                .WithNetworkAliases(ServiceBusBuilder.DatabaseNetworkAlias)
                .Build();

            await _sql.StartAsync();

            _serviceBus = new ServiceBusBuilder(ServiceBusImage)
                .WithAcceptLicenseAgreement(true)
                .WithConfig(Path.Combine(AppContext.BaseDirectory, "servicebus-test-config.json"))
                .WithNetworkAliases(ServiceBusAlias)
                .WithMsSqlContainer(
                    _network, _sql, ServiceBusBuilder.DatabaseNetworkAlias, MsSqlBuilder.DefaultPassword)
                .Build();

            await _serviceBus.StartAsync();

            ServiceBusConnectionString = _serviceBus.GetConnectionString();
            SqlConnectionString = _sql.GetConnectionString();

            await StartWorkerAsync();

            Started = true;
        }
        catch (Exception ex)
        {
            // Most often no Docker daemon. Record it so every test in the collection can
            // skip with the real reason instead of failing on a null connection string.
            StartupFailure = ex.Message;
        }
    }

    // Builds and runs AnalysisService.Worker exactly as it is deployed.
    private async Task StartWorkerAsync()
    {
        _workerImage = new ImageFromDockerfileBuilder()
            .WithDockerfileDirectory(RepositoryRoot())
            .WithDockerfile("src/AnalysisService.Worker/Dockerfile")
            .WithName("documentintelligence-worker-test:latest")
            .WithCleanUp(false)   // reuse across runs; the build is the slow part
            .Build();

        await _workerImage.CreateAsync();

        _worker = new ContainerBuilder(_workerImage)
            .WithNetwork(_network)
            .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
            .WithEnvironment(
                "AzureServiceBus__ConnectionString",
                $"Endpoint=sb://{ServiceBusAlias}/;SharedAccessKeyName=RootManageSharedAccessKey;"
                + "SharedAccessKey=local;UseDevelopmentEmulator=true;")
            .WithEnvironment("AzureServiceBus__AnalyzeDocumentQueueName", "analyze-document")
            .WithEnvironment("AzureServiceBus__AnalysisCompletedTopic", "analysis-completed")
            .WithEnvironment("AzureServiceBus__AnalysisFailedTopic", "analysis-failed")
            .WithWaitStrategy(
                Wait.ForUnixContainer().UntilMessageIsLogged("Environment: Development"))
            .Build();

        await _worker.StartAsync();
    }

    /// Walks up from the test output directory to the folder holding the solution, which is
    /// the build context both Dockerfiles expect.
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DocumentIntelligence.sln")))
            directory = directory.Parent;

        return directory?.FullName
               ?? throw new InvalidOperationException(
                   "Could not find DocumentIntelligence.sln above " + AppContext.BaseDirectory);
    }

    /// The worker's console output, so a test that times out can say what the worker was doing.
    public async Task<string> WorkerLogsAsync()
    {
        if (_worker is null) return "(worker not started)";

        var (stdout, stderr) = await _worker.GetLogsAsync();
        return stdout + stderr;
    }

    public async Task DisposeAsync()
    {
        if (_worker is not null) await _worker.DisposeAsync();
        if (_serviceBus is not null) await _serviceBus.DisposeAsync();
        if (_sql is not null) await _sql.DisposeAsync();
        if (_network is not null) await _network.DisposeAsync();
    }

    /// A migrated database of its own per test, so one test's rows cannot explain
    /// another's result.
    public async Task<string> CreateDatabaseAsync()
    {
        var databaseName = "it_" + Guid.NewGuid().ToString("N")[..12];

        await using (var conn = new SqlConnection(SqlConnectionString))
        {
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE [{databaseName}]";
            await cmd.ExecuteNonQueryAsync();
        }

        var connectionString = new SqlConnectionStringBuilder(SqlConnectionString)
        {
            InitialCatalog = databaseName
        }.ConnectionString;

        await using var db = new DocumentApiDbContext(
            new DbContextOptionsBuilder<DocumentApiDbContext>().UseSqlServer(connectionString).Options);
        await db.Database.MigrateAsync();

        return connectionString;
    }
}

[CollectionDefinition(Name)]
public sealed class SharedContainerCollection : ICollectionFixture<SharedContainerFixture>
{
    public const string Name = "shared-containers";
}
