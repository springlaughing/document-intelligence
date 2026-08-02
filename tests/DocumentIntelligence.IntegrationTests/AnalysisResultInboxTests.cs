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

// Covers the band the unit tests cannot reach: a message that actually crosses the
// broker, is deserialized by the real dispatcher, dispatched through a real DI scope to
// the real handler, and applied through a real transaction against a real database.
//
// Every unit test in this repo mocks at IMessagePublisher or IMessageHandler, so none of
// them can say whether a byte ever moved.
//
// Each test owns a subscription, declared in servicebus-test-config.json and selected by
// a correlation filter on the message Subject. Sharing one subscription made the tests
// steal each other's messages, which is a property of the harness rather than the code
// and produced failures that meant nothing.
[Collection(BrokerCollection.Name)]
public class AnalysisResultInboxTests : IAsyncDisposable
{
    private const string Topic = "analysis-completed";

    private readonly BrokerFixture _broker;
    private readonly List<IAsyncDisposable> _disposables = new();
    private ServiceProvider? _services;

    public AnalysisResultInboxTests(BrokerFixture broker) => _broker = broker;

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
    public async Task A_redelivered_event_is_applied_exactly_once()
    {
        Skip.IfNot(_broker.Started, $"Containers did not start: {_broker.StartupFailure}");

        const string subscription = "redelivered-event";
        var (dbConnection, documentId) = await GivenAnUploadedDocument();
        var client = await StartConsumer(dbConnection, subscription);

        // One event id, two different payloads, so the stored row is the evidence rather
        // than a log line.
        //
        // The second copy is published only once the first has been applied. That is what
        // redelivery actually is - the broker handing back a message it already delivered -
        // and publishing both at once would instead be two concurrent deliveries, which the
        // dispatcher processes in parallel (MaxConcurrentCalls). The guard still applies
        // exactly one of those, but which one is a race, and asserting "first wins" would
        // be demanding an ordering the system never promised.
        var eventId = Guid.NewGuid();

        await PublishAsync(client, subscription,
            new AnalysisCompletedEvent(eventId, documentId, "first-copy", "blob://first.json"));
        await WaitUntil(dbConnection, d => d.AnalysisSummary == "first-copy");

        await PublishAsync(client, subscription,
            new AnalysisCompletedEvent(eventId, documentId, "second-copy", "blob://second.json"));

        // Give the redelivery time to be wrongly applied before asserting it was not.
        await Task.Delay(TimeSpan.FromSeconds(4));

        var document = await LoadDocument(dbConnection, documentId);
        Assert.Equal(DocumentStatus.Analyzed, document.Status);
        Assert.Equal("first-copy", document.AnalysisSummary);
        Assert.Equal("blob://first.json", document.AnalysisBlobRef);

        await using var db = NewDbContext(dbConnection);
        Assert.Equal(1, await db.ProcessedMessages.CountAsync(m => m.MessageId == eventId));
    }

    [SkippableFact]
    public async Task Two_distinct_events_are_both_applied()
    {
        // The guard must not be so eager that it swallows a genuine second result.
        Skip.IfNot(_broker.Started, $"Containers did not start: {_broker.StartupFailure}");

        const string subscription = "distinct-events";
        var (dbConnection, documentId) = await GivenAnUploadedDocument();
        var client = await StartConsumer(dbConnection, subscription);

        await PublishAsync(client, subscription,
            new AnalysisCompletedEvent(Guid.NewGuid(), documentId, "pass-one", "blob://one.json"));
        await WaitUntil(dbConnection, d => d.AnalysisSummary == "pass-one");

        await PublishAsync(client, subscription,
            new AnalysisCompletedEvent(Guid.NewGuid(), documentId, "pass-two", "blob://two.json"));
        await WaitUntil(dbConnection, d => d.AnalysisSummary == "pass-two");

        Assert.Equal("pass-two", (await LoadDocument(dbConnection, documentId)).AnalysisSummary);
    }

    [SkippableFact]
    public async Task An_event_for_an_unknown_document_is_dead_lettered()
    {
        // Nothing can delete a document in this service, so an event naming one that does
        // not exist is a symptom. It must be kept for inspection, not quietly completed.
        Skip.IfNot(_broker.Started, $"Containers did not start: {_broker.StartupFailure}");

        const string subscription = "unknown-document";
        var dbConnection = await _broker.CreateDatabaseAsync();
        var client = await StartConsumer(dbConnection, subscription);

        var missingDocumentId = Guid.NewGuid();
        await PublishAsync(client, subscription,
            new AnalysisCompletedEvent(Guid.NewGuid(), missingDocumentId, "orphan", "blob://orphan.json"));

        // It should land in the dead-letter queue rather than being retried to death.
        await using var deadLetterReceiver = client.CreateReceiver(Topic, subscription,
            new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });

        var deadLettered = await deadLetterReceiver.ReceiveMessageAsync(TimeSpan.FromSeconds(30));

        Assert.NotNull(deadLettered);
        Assert.Equal("Unprocessable", deadLettered!.DeadLetterReason);
        Assert.Contains(missingDocumentId.ToString(), deadLettered.DeadLetterErrorDescription);
    }

    // ---- helpers ----

    private static DocumentApiDbContext NewDbContext(string connectionString) =>
        new(new DbContextOptionsBuilder<DocumentApiDbContext>().UseSqlServer(connectionString).Options);

    private async Task<(string DbConnection, Guid DocumentId)> GivenAnUploadedDocument()
    {
        var connectionString = await _broker.CreateDatabaseAsync();

        await using var db = NewDbContext(connectionString);
        var documentId = Guid.NewGuid();
        db.Documents.Add(new DocumentEntity
        {
            Id = documentId,
            FileName = "integration.pdf",
            Status = DocumentStatus.Uploaded
        });
        await db.SaveChangesAsync();

        return (connectionString, documentId);
    }

    // Wires the real dispatcher to the real handler through a real DI scope - the same
    // path AnalysisCompletedEventListener takes in the running service.
    private async Task<ServiceBusClient> StartConsumer(string dbConnection, string subscription)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<DocumentApiDbContext>(o => o.UseSqlServer(dbConnection));
        services.AddScoped<IDocumentRepository, EfDocumentRepository>();
        services.AddScoped<IMessageHandler<AnalysisCompletedEvent>, AnalysisCompletedEventHandler>();

        _services = services.BuildServiceProvider();

        var client = new ServiceBusClient(_broker.ServiceBusConnectionString);
        _disposables.Add(client);

        var dispatcher = new ServiceBusMessageDispatcher<AnalysisCompletedEvent>(
            client,
            _services.GetRequiredService<IServiceScopeFactory>(),
            _services.GetRequiredService<ILogger<ServiceBusMessageDispatcher<AnalysisCompletedEvent>>>(),
            entityName: Topic,
            subscriptionName: subscription,
            options: null,
            jsonOpt: JsonOptions);

        _disposables.Add(dispatcher);
        await dispatcher.StartAsync(CancellationToken.None);

        return client;
    }

    // Subject doubles as the routing key: each subscription's correlation filter selects
    // only the messages its own test published.
    private static async Task PublishAsync(ServiceBusClient client, string subject, AnalysisCompletedEvent evt)
    {
        await using var sender = client.CreateSender(Topic);
        await sender.SendMessageAsync(new ServiceBusMessage(JsonSerializer.Serialize(evt, JsonOptions))
        {
            ContentType = "application/json",
            Subject = subject
        });
    }

    private static async Task<DocumentRecord> LoadDocument(string connectionString, Guid documentId)
    {
        await using var db = NewDbContext(connectionString);
        var entity = await db.Documents.AsNoTracking().SingleAsync(d => d.Id == documentId);
        return new DocumentRecord(entity.Id, entity.FileName, entity.Status,
            entity.AnalysisSummary, entity.AnalysisBlobRef);
    }

    private static async Task WaitUntil(string connectionString, Func<DocumentEntity, bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(40);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await using var db = NewDbContext(connectionString);
                var entity = await db.Documents.AsNoTracking().FirstOrDefaultAsync();
                if (entity is not null && condition(entity)) return;
            }
            catch
            {
                // database not ready yet
            }

            await Task.Delay(500);
        }

        throw new TimeoutException("The consumer did not reach the expected state within 40s.");
    }
}
