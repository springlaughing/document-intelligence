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
// them can tell you whether a byte ever moved.
//
// STATUS: skipped, not finished. The scenario itself is proven - it was verified by hand
// against the emulator - but as written the two tests share one subscription and steal
// each other's messages. Fix that, and swap the skip-if-absent probe for Testcontainers,
// before relying on these.
public class AnalysisResultInboxTests : IAsyncLifetime
{
    private const string Topic = "analysis-completed";
    private const string Subscription = "document-api";

    private string _databaseName = "";
    private string _dbConnection = "";
    private ServiceProvider? _services;
    private ServiceBusMessageDispatcher<AnalysisCompletedEvent>? _dispatcher;
    private ServiceBusClient? _client;

    private static JsonSerializerOptions JsonOptions => new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_dispatcher is not null) await _dispatcher.DisposeAsync();
        if (_client is not null) await _client.DisposeAsync();
        _services?.Dispose();
        if (_databaseName.Length > 0) await TestEnvironment.DropDatabaseAsync(_databaseName);
    }

    [SkippableFact(Skip = "INCOMPLETE - both tests share the 'document-api' subscription, so each consumes the other's messages, and the dispatcher is not torn down between them. Needs a per-test subscription (or Testcontainers with a fresh emulator per run) before it can be trusted.")]
    public async Task A_redelivered_event_is_applied_exactly_once()
    {
        Skip.IfNot(await TestEnvironment.SqlIsReachableAsync(), "No SQL Server reachable.");
        Skip.IfNot(await TestEnvironment.BrokerIsReachableAsync(Topic),
            $"No Service Bus reachable, or topic '{Topic}' does not exist. Start it with: docker compose up -d sqledge servicebus");

        var documentId = await GivenAnUploadedDocument();
        await StartTheApiConsumer();

        // One event id, two different payloads. If the guard fails, the document ends up
        // with the second payload - which makes the stored row the evidence rather than a
        // log line.
        var eventId = Guid.NewGuid();
        await PublishAsync(new AnalysisCompletedEvent(eventId, documentId, "first-copy", "blob://first.json"));
        await PublishAsync(new AnalysisCompletedEvent(eventId, documentId, "second-copy", "blob://second.json"));

        await WaitUntil(async () => (await LoadDocument(documentId)).Status == DocumentStatus.Analyzed);
        await Task.Delay(TimeSpan.FromSeconds(3)); // give the second copy a chance to be wrongly applied

        var document = await LoadDocument(documentId);

        Assert.Equal(DocumentStatus.Analyzed, document.Status);
        Assert.Equal("first-copy", document.AnalysisSummary);
        Assert.Equal("blob://first.json", document.AnalysisBlobRef);

        await using var db = NewDbContext();
        var inboxRows = await db.ProcessedMessages.CountAsync(m => m.MessageId == eventId);
        Assert.Equal(1, inboxRows);
    }

    [SkippableFact(Skip = "INCOMPLETE - both tests share the 'document-api' subscription, so each consumes the other's messages, and the dispatcher is not torn down between them. Needs a per-test subscription (or Testcontainers with a fresh emulator per run) before it can be trusted.")]
    public async Task Two_distinct_events_are_both_applied()
    {
        // The guard must not be so eager that it swallows a genuine second result.
        Skip.IfNot(await TestEnvironment.SqlIsReachableAsync(), "No SQL Server reachable.");
        Skip.IfNot(await TestEnvironment.BrokerIsReachableAsync(Topic), "No Service Bus reachable.");

        var documentId = await GivenAnUploadedDocument();
        await StartTheApiConsumer();

        await PublishAsync(new AnalysisCompletedEvent(Guid.NewGuid(), documentId, "pass-one", "blob://one.json"));
        await WaitUntil(async () => (await LoadDocument(documentId)).AnalysisSummary == "pass-one");

        await PublishAsync(new AnalysisCompletedEvent(Guid.NewGuid(), documentId, "pass-two", "blob://two.json"));
        await WaitUntil(async () => (await LoadDocument(documentId)).AnalysisSummary == "pass-two");

        var document = await LoadDocument(documentId);
        Assert.Equal("pass-two", document.AnalysisSummary);
    }

    // ---- setup helpers ----

    private DocumentApiDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<DocumentApiDbContext>()
            .UseSqlServer(_dbConnection)
            .Options);

    private async Task<Guid> GivenAnUploadedDocument()
    {
        _dbConnection = TestEnvironment.CreateTestDatabaseConnectionString(out _databaseName);

        await using var db = NewDbContext();
        await db.Database.MigrateAsync();

        var documentId = Guid.NewGuid();
        db.Documents.Add(new DocumentEntity
        {
            Id = documentId,
            FileName = "integration.pdf",
            Status = DocumentStatus.Uploaded
        });
        await db.SaveChangesAsync();

        return documentId;
    }

    // Wires the real dispatcher to the real handler through a real DI scope - the same
    // path AnalysisCompletedEventListener uses in the running service.
    private async Task StartTheApiConsumer()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddSimpleConsole().SetMinimumLevel(LogLevel.Information));
        services.AddDbContext<DocumentApiDbContext>(o => o.UseSqlServer(_dbConnection));
        services.AddScoped<IDocumentRepository, EfDocumentRepository>();
        services.AddScoped<IMessageHandler<AnalysisCompletedEvent>, AnalysisCompletedEventHandler>();

        _services = services.BuildServiceProvider();
        _client = new ServiceBusClient(TestEnvironment.ServiceBusConnectionString);

        _dispatcher = new ServiceBusMessageDispatcher<AnalysisCompletedEvent>(
            _client,
            _services.GetRequiredService<IServiceScopeFactory>(),
            _services.GetRequiredService<ILogger<ServiceBusMessageDispatcher<AnalysisCompletedEvent>>>(),
            entityName: Topic,
            subscriptionName: Subscription,
            options: null,
            jsonOpt: JsonOptions);

        await _dispatcher.StartAsync(CancellationToken.None);
    }

    private async Task PublishAsync(AnalysisCompletedEvent evt)
    {
        await using var sender = _client!.CreateSender(Topic);
        await sender.SendMessageAsync(new ServiceBusMessage(JsonSerializer.Serialize(evt, JsonOptions))
        {
            ContentType = "application/json",
            Subject = nameof(AnalysisCompletedEvent)
        });
    }

    private async Task<DocumentRecord> LoadDocument(Guid documentId)
    {
        await using var db = NewDbContext();
        var entity = await db.Documents.AsNoTracking().SingleAsync(d => d.Id == documentId);
        return new DocumentRecord(entity.Id, entity.FileName, entity.Status,
            entity.AnalysisSummary, entity.AnalysisBlobRef);
    }

    private static async Task WaitUntil(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            try { if (await condition()) return; } catch { /* not ready yet */ }
            await Task.Delay(500);
        }

        throw new TimeoutException("The consumer did not reach the expected state within 30s.");
    }
}
