using AnalysisService.Worker.Infrastructure;
using AnalysisService.Worker.Messaging;
using DocumentIntelligence.Contracts;
using Microsoft.Extensions.Logging;
using Moq;

namespace AnalysisService.Worker.Tests;

public class AnalyzeDocumentCommandHandlerTests
{
    private static AnalyzeDocumentCommandHandler CreateSut(
        Mock<IAnalysisResultStore> results,
        Mock<IAnalysisResultEventPublisher> publisher) =>
        new(results.Object, publisher.Object, Mock.Of<ILogger<AnalyzeDocumentCommandHandler>>());

    // A store that behaves like the real thing: nothing until saved, then the saved value.
    private static Mock<IAnalysisResultStore> WorkingStore()
    {
        var saved = new Dictionary<Guid, StoredAnalysis>();
        var store = new Mock<IAnalysisResultStore>();

        store.Setup(s => s.TryGetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                saved.TryGetValue(id, out var s) ? s : null);

        store.Setup(s => s.SaveAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid cmdId, Guid docId, string summary, string[] entities, CancellationToken _) =>
            {
                var stored = new StoredAnalysis(
                    $"blob://analysis-results/{docId}/{cmdId}.json", summary, entities);
                saved[cmdId] = stored;
                return stored;
            });

        return store;
    }

    private static (Mock<IAnalysisResultEventPublisher>, List<AnalysisCompletedEvent>) CapturingPublisher()
    {
        var captured = new List<AnalysisCompletedEvent>();
        var publisher = new Mock<IAnalysisResultEventPublisher>();
        publisher
            .Setup(p => p.PublishAsync(It.IsAny<AnalysisCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AnalysisCompletedEvent, CancellationToken>((e, _) => captured.Add(e))
            .Returns(Task.CompletedTask);
        return (publisher, captured);
    }

    [Fact]
    public async Task Redelivery_does_not_repeat_the_analysis()
    {
        // The expensive part must run once. The store is what remembers, because this
        // service holds no state of its own.
        var cmd = new AnalyzeDocumentCommand(Guid.NewGuid(), Guid.NewGuid(), "invoice.pdf");
        var store = WorkingStore();
        var (publisher, published) = CapturingPublisher();

        var sut = CreateSut(store, publisher);

        await sut.HandleAsync(cmd, CancellationToken.None);
        await sut.HandleAsync(cmd, CancellationToken.None);   // the broker delivered it twice

        store.Verify(s => s.SaveAsync(
            cmd.CommandId, cmd.DocumentId, It.IsAny<string>(), It.IsAny<string[]>(),
            It.IsAny<CancellationToken>()),
            Times.Once);   // analysed once...

        Assert.Equal(2, published.Count);              // ...but announced both times,
        Assert.Equal(published[0], published[1]);      // identically, so the consumer can discard one
    }

    [Fact]
    public async Task Re_analysis_is_not_mistaken_for_a_duplicate()
    {
        // A new request to analyse carries a new CommandId, so the store has nothing for
        // it and the work runs again - which is correct, it is a different request.
        var docId = Guid.NewGuid();
        var store = WorkingStore();
        var (publisher, published) = CapturingPublisher();

        var sut = CreateSut(store, publisher);

        await sut.HandleAsync(new AnalyzeDocumentCommand(Guid.NewGuid(), docId, "invoice.pdf"), CancellationToken.None);
        await sut.HandleAsync(new AnalyzeDocumentCommand(Guid.NewGuid(), docId, "invoice.pdf"), CancellationToken.None);

        store.Verify(s => s.SaveAsync(
            It.IsAny<Guid>(), docId, It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        Assert.NotEqual(published[0].EventId, published[1].EventId);
        Assert.NotEqual(published[0].BlobReference, published[1].BlobReference);
    }

    [Fact]
    public async Task A_skipped_run_republishes_the_stored_result_not_a_recomputed_one()
    {
        // If the work is skipped, the store is the only place the answer exists.
        var cmd = new AnalyzeDocumentCommand(Guid.NewGuid(), Guid.NewGuid(), "invoice.pdf");

        var store = new Mock<IAnalysisResultStore>();
        store.Setup(s => s.TryGetAsync(cmd.CommandId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoredAnalysis("blob://stored/earlier.json", "summary from the first run", new[] { "E:1" }));

        var (publisher, published) = CapturingPublisher();

        await CreateSut(store, publisher).HandleAsync(cmd, CancellationToken.None);

        Assert.Equal("summary from the first run", published[0].Summary);
        Assert.Equal("blob://stored/earlier.json", published[0].BlobReference);

        store.Verify(s => s.SaveAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string[]>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Successful_analysis_publishes_completed_event()
    {
        var docId = Guid.NewGuid();
        var store = WorkingStore();
        var publisher = new Mock<IAnalysisResultEventPublisher>();

        var sut = CreateSut(store, publisher);

        await sut.HandleAsync(new AnalyzeDocumentCommand(Guid.NewGuid(), docId, "invoice.pdf"), CancellationToken.None);

        publisher.Verify(p => p.PublishAsync(
            It.Is<AnalysisCompletedEvent>(e =>
                e.DocumentId == docId && e.BlobReference.Contains(docId.ToString())),
            It.IsAny<CancellationToken>()),
            Times.Once);

        publisher.Verify(p => p.PublishAsync(
            It.IsAny<AnalysisFailedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Failed_analysis_publishes_failed_event_instead_of_completed()
    {
        // The liveness guarantee: a document must never be left mid-analysis because
        // the analysis threw.
        var docId = Guid.NewGuid();
        var store = new Mock<IAnalysisResultStore>();
        var publisher = new Mock<IAnalysisResultEventPublisher>();

        store.Setup(s => s.SaveAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("blob store unavailable"));

        await CreateSut(store, publisher).HandleAsync(
            new AnalyzeDocumentCommand(Guid.NewGuid(), docId, "invoice.pdf"), CancellationToken.None);

        publisher.Verify(p => p.PublishAsync(
            It.Is<AnalysisFailedEvent>(e =>
                e.DocumentId == docId && e.Reason.Contains("blob store unavailable")),
            It.IsAny<CancellationToken>()),
            Times.Once);

        publisher.Verify(p => p.PublishAsync(
            It.IsAny<AnalysisCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_redelivered_failure_reports_the_same_event_id()
    {
        var cmd = new AnalyzeDocumentCommand(Guid.NewGuid(), Guid.NewGuid(), "invoice.pdf");
        var store = new Mock<IAnalysisResultStore>();
        store.Setup(s => s.SaveAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("blob store unavailable"));

        var published = new List<AnalysisFailedEvent>();
        var publisher = new Mock<IAnalysisResultEventPublisher>();
        publisher
            .Setup(p => p.PublishAsync(It.IsAny<AnalysisFailedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AnalysisFailedEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);

        var sut = CreateSut(store, publisher);

        await sut.HandleAsync(cmd, CancellationToken.None);
        await sut.HandleAsync(cmd, CancellationToken.None);

        Assert.Equal(published[0].EventId, published[1].EventId);
    }

    [Fact]
    public async Task Completed_and_failed_ids_derived_from_one_command_do_not_collide()
    {
        var commandId = Guid.NewGuid();
        var docId = Guid.NewGuid();

        var failingStore = new Mock<IAnalysisResultStore>();
        failingStore.Setup(s => s.SaveAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        AnalysisFailedEvent? failed = null;
        var failPublisher = new Mock<IAnalysisResultEventPublisher>();
        failPublisher.Setup(p => p.PublishAsync(It.IsAny<AnalysisFailedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AnalysisFailedEvent, CancellationToken>((e, _) => failed = e)
            .Returns(Task.CompletedTask);

        var (okPublisher, completed) = CapturingPublisher();

        var cmd = new AnalyzeDocumentCommand(commandId, docId, "invoice.pdf");

        await CreateSut(WorkingStore(), okPublisher).HandleAsync(cmd, CancellationToken.None);
        await CreateSut(failingStore, failPublisher).HandleAsync(cmd, CancellationToken.None);

        Assert.NotEqual(completed[0].EventId, failed!.EventId);
    }

    [Fact]
    public async Task Cancellation_is_not_reported_as_an_analysis_failure()
    {
        // Shutting down mid-message is not an outcome - the message should be redelivered.
        var store = new Mock<IAnalysisResultStore>();
        var publisher = new Mock<IAnalysisResultEventPublisher>();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        store.Setup(s => s.TryGetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => CreateSut(store, publisher).HandleAsync(
                new AnalyzeDocumentCommand(Guid.NewGuid(), Guid.NewGuid(), "invoice.pdf"), cts.Token));

        publisher.Verify(p => p.PublishAsync(
            It.IsAny<AnalysisFailedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
