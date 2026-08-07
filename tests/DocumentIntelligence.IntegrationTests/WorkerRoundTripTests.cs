using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Messaging.ServiceBus;
using DocumentIntelligence.Contracts;
using DocumentIntelligence.Messaging;
using DocumentService.Api.Domain;
using DocumentService.Api.Features.Documents.RecordAnalysisResult;
using DocumentService.Api.Infrastructure.Ef;
using DocumentService.Api.Infrastructure.Ef.Entities;
using DocumentService.Api.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DocumentIntelligence.IntegrationTests;

// The one path no other test covers: a command leaving this service, being handled by the
// real worker in its own process, and its result coming back and landing on the document.
//
// Every other test stops at a boundary. The unit tests mock at IMessagePublisher or
// IMessageHandler. AnalysisResultInboxTests crosses the broker but plays the worker's part
// itself, publishing the events it then consumes. So the two services have only ever been
// run together by hand - see docs/verification-log.md.
//
// Here the worker is its real image, started by SharedContainerFixture from the same
// Dockerfile that is deployed. Nothing in this file knows how the worker works; it puts a
// command on the queue and waits for the document to change.
[Collection(SharedContainerCollection.Name)]
public class WorkerRoundTripTests : IAsyncDisposable
{
    private const string CommandQueue = "analyze-document";
    private const string ResultTopic = "analysis-completed";

    // Selected by a correlation filter on Subject, which AzureServiceBusPublisher sets to
    // the contract type name - so this subscription sees the worker's real output and not
    // the hand-published messages the other tests use.
    private const string ResultSubscription = "worker-round-trip";

    private readonly SharedContainerFixture _containers;
    private readonly List<IAsyncDisposable> _disposables = new();
    private ServiceProvider? _services;

    public WorkerRoundTripTests(SharedContainerFixture containers) => _containers = containers;

    private static JsonSerializerOptions JsonOptions => new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async ValueTask DisposeAsync()
    {
        foreach (var d in _disposables) await d.DisposeAsync();
        _services?.Dispose();
    }

    [SkippableFact]
    public async Task A_command_is_analysed_by_the_worker_and_the_result_lands_on_the_document()
    {
        Skip.IfNot(_containers.Started, $"Containers did not start: {_containers.StartupFailure}");

        var dbConnection = await _containers.CreateDatabaseAsync();
        var documentId = await GivenAnUploadedDocument(dbConnection);

        var client = await StartResultConsumer(dbConnection);

        // Published the way the outbox drainer publishes it, since that is what the worker
        // will actually receive in production.
        await using var sender = client.CreateSender(CommandQueue);
        var command = new AnalyzeDocumentCommand(Guid.NewGuid(), documentId, "round-trip.pdf");

        await sender.SendMessageAsync(new ServiceBusMessage(JsonSerializer.Serialize(command, JsonOptions))
        {
            ContentType = "application/json",
            Subject = nameof(AnalyzeDocumentCommand),
            MessageId = Guid.NewGuid().ToString()
        });

        var document = await WaitForStatus(dbConnection, documentId, DocumentStatus.Analyzed);

        // The summary and blob reference are the worker's, not this test's - which is what
        // proves the round trip rather than a local write.
        Assert.Equal($"Auto summary for {command.FileName}", document.AnalysisSummary);
        Assert.Equal(
            $"blob://analysis-results/{documentId}/{command.CommandId}.json",
            document.AnalysisBlobRef);

        // And the result was recorded through the inbox, so a redelivery would be discarded.
        await using var db = NewDbContext(dbConnection);
        Assert.Equal(1, await db.ProcessedMessages.CountAsync());
    }

    // ---- helpers ----

    private static DocumentApiDbContext NewDbContext(string connectionString) =>
        new(new DbContextOptionsBuilder<DocumentApiDbContext>().UseSqlServer(connectionString).Options);

    private static async Task<Guid> GivenAnUploadedDocument(string connectionString)
    {
        await using var db = NewDbContext(connectionString);
        var documentId = Guid.NewGuid();

        db.Documents.Add(new DocumentEntity
        {
            Id = documentId,
            FileName = "round-trip.pdf",
            Status = DocumentStatus.Uploaded
        });
        await db.SaveChangesAsync();

        return documentId;
    }

    // The API's side of the round trip: the real dispatcher and handler, the same path
    // AnalysisCompletedEventListener takes in the running service.
    private async Task<ServiceBusClient> StartResultConsumer(string dbConnection)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<DocumentApiDbContext>(o => o.UseSqlServer(dbConnection));
        services.AddScoped<IDocumentRepository, EfDocumentRepository>();
        services.AddScoped<IMessageHandler<AnalysisCompletedEvent>, AnalysisCompletedEventHandler>();

        _services = services.BuildServiceProvider();

        var client = new ServiceBusClient(_containers.ServiceBusConnectionString);
        _disposables.Add(client);

        var dispatcher = new ServiceBusMessageDispatcher<AnalysisCompletedEvent>(
            client,
            _services.GetRequiredService<IServiceScopeFactory>(),
            _services.GetRequiredService<ILogger<ServiceBusMessageDispatcher<AnalysisCompletedEvent>>>(),
            entityName: ResultTopic,
            subscriptionName: ResultSubscription,
            options: null,
            jsonOpt: JsonOptions);

        _disposables.Add(dispatcher);
        await dispatcher.StartAsync(CancellationToken.None);

        return client;
    }

    private async Task<DocumentEntity> WaitForStatus(
        string connectionString, Guid documentId, DocumentStatus expected)
    {
        // Generous, because this waits on another process consuming from a broker rather
        // than on anything in this one.
        var deadline = DateTime.UtcNow.AddSeconds(90);

        while (DateTime.UtcNow < deadline)
        {
            await using (var db = NewDbContext(connectionString))
            {
                var entity = await db.Documents.AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == documentId);

                if (entity is not null && entity.Status == expected) return entity;
            }

            await Task.Delay(500);
        }

        // A bare timeout here would say nothing about which half failed, and the worker is
        // in another container where nobody would think to look.
        throw new TimeoutException(
            $"Document {documentId} did not reach {expected} within 90s. Worker logs:\n"
            + await _containers.WorkerLogsAsync());
    }
}
