using DocumentService.Api.Infrastructure.Ef;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using Testcontainers.ServiceBus;

namespace DocumentIntelligence.IntegrationTests;

// Starts a Service Bus emulator and the SQL Server it depends on, for the lifetime of the
// test collection.
//
// The containers are started by the test run rather than found already running, which is
// what makes these tests reproducible and what lets them work in CI, where nothing is
// running beforehand. It also means the entity config is ours: the emulator only creates
// queues, topics and subscriptions from its config file at startup and has no management
// API, so a test cannot create a subscription for itself at runtime. Declaring one
// subscription per test in servicebus-test-config.json is the only way to stop tests
// consuming each other's messages.
public sealed class SharedContainerFixture : IAsyncLifetime
{
    // Images are named here rather than left to the library's defaults. Testcontainers
    // now wants the image passed to the builder, and its default SQL tag is not the one
    // usually already pulled locally - pinning avoids a second multi-gigabyte download.
    private const string SqlImage = "mcr.microsoft.com/mssql/server:2022-latest";
    private const string ServiceBusImage = "mcr.microsoft.com/azure-messaging/servicebus-emulator:latest";

    private INetwork? _network;
    private MsSqlContainer? _sql;
    private ServiceBusContainer? _serviceBus;

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
                .WithMsSqlContainer(
                    _network, _sql, ServiceBusBuilder.DatabaseNetworkAlias, MsSqlBuilder.DefaultPassword)
                .Build();

            await _serviceBus.StartAsync();

            ServiceBusConnectionString = _serviceBus.GetConnectionString();
            SqlConnectionString = _sql.GetConnectionString();
            Started = true;
        }
        catch (Exception ex)
        {
            // Most often no Docker daemon. Record it so every test in the collection can
            // skip with the real reason instead of failing on a null connection string.
            StartupFailure = ex.Message;
        }
    }

    public async Task DisposeAsync()
    {
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
