using AnalysisService.Worker.Infrastructure;
using AnalysisService.Worker.Messaging;
using DocumentIntelligence.Contracts;
using Microsoft.Extensions.Logging;
using Moq;

namespace AnalysisService.Worker.Tests;

public class AnalyzeDocumentCommandHandlerTests
{
    private static AnalyzeDocumentCommandHandler CreateSut(
        Mock<IBlobWriter> blobWriter,
        Mock<IAnalysisResultEventPublisher> publisher) =>
        new(blobWriter.Object, publisher.Object, Mock.Of<ILogger<AnalyzeDocumentCommandHandler>>());

    [Fact]
    public async Task Redelivering_the_same_command_produces_the_same_event_id()
    {
        // This service has no store, so it cannot know the command is a repeat. What it
        // can do is emit an identical result, which the consumer's inbox discards.
        var cmd = new AnalyzeDocumentCommand(Guid.NewGuid(), Guid.NewGuid(), "invoice.pdf");

        var blobWriter = new Mock<IBlobWriter>();
        blobWriter
            .Setup(b => b.SaveAsync(cmd.DocumentId, It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("blob://analysis-results/result.json");

        var published = new List<AnalysisCompletedEvent>();
        var publisher = new Mock<IAnalysisResultEventPublisher>();
        publisher
            .Setup(p => p.PublishAsync(It.IsAny<AnalysisCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AnalysisCompletedEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);

        var sut = CreateSut(blobWriter, publisher);

        await sut.HandleAsync(cmd, CancellationToken.None);
        await sut.HandleAsync(cmd, CancellationToken.None);   // the broker delivered it twice

        Assert.Equal(2, published.Count);
        Assert.Equal(published[0].EventId, published[1].EventId);
        Assert.Equal(published[0], published[1]);             // identical in every field
    }

    [Fact]
    public async Task A_different_command_for_the_same_document_gets_a_different_event_id()
    {
        // Re-analysis is a real request, not a duplicate, and must not be deduplicated.
        var docId = Guid.NewGuid();

        var blobWriter = new Mock<IBlobWriter>();
        blobWriter
            .Setup(b => b.SaveAsync(docId, It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("blob://analysis-results/result.json");

        var published = new List<AnalysisCompletedEvent>();
        var publisher = new Mock<IAnalysisResultEventPublisher>();
        publisher
            .Setup(p => p.PublishAsync(It.IsAny<AnalysisCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AnalysisCompletedEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);

        var sut = CreateSut(blobWriter, publisher);

        await sut.HandleAsync(new AnalyzeDocumentCommand(Guid.NewGuid(), docId, "invoice.pdf"), CancellationToken.None);
        await sut.HandleAsync(new AnalyzeDocumentCommand(Guid.NewGuid(), docId, "invoice.pdf"), CancellationToken.None);

        Assert.NotEqual(published[0].EventId, published[1].EventId);
    }

    [Fact]
    public async Task A_redelivered_failure_also_reports_the_same_event_id()
    {
        var cmd = new AnalyzeDocumentCommand(Guid.NewGuid(), Guid.NewGuid(), "invoice.pdf");

        var blobWriter = new Mock<IBlobWriter>();
        blobWriter
            .Setup(b => b.SaveAsync(cmd.DocumentId, It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("blob store unavailable"));

        var published = new List<AnalysisFailedEvent>();
        var publisher = new Mock<IAnalysisResultEventPublisher>();
        publisher
            .Setup(p => p.PublishAsync(It.IsAny<AnalysisFailedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AnalysisFailedEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);

        var sut = CreateSut(blobWriter, publisher);

        await sut.HandleAsync(cmd, CancellationToken.None);
        await sut.HandleAsync(cmd, CancellationToken.None);

        Assert.Equal(published[0].EventId, published[1].EventId);
    }

    [Fact]
    public async Task Completed_and_failed_ids_derived_from_one_command_do_not_collide()
    {
        var commandId = Guid.NewGuid();
        var docId = Guid.NewGuid();

        var okWriter = new Mock<IBlobWriter>();
        okWriter.Setup(b => b.SaveAsync(docId, It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("blob://ok.json");

        var failWriter = new Mock<IBlobWriter>();
        failWriter.Setup(b => b.SaveAsync(docId, It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        AnalysisCompletedEvent? completed = null;
        AnalysisFailedEvent? failed = null;

        var okPublisher = new Mock<IAnalysisResultEventPublisher>();
        okPublisher.Setup(p => p.PublishAsync(It.IsAny<AnalysisCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AnalysisCompletedEvent, CancellationToken>((e, _) => completed = e)
            .Returns(Task.CompletedTask);

        var failPublisher = new Mock<IAnalysisResultEventPublisher>();
        failPublisher.Setup(p => p.PublishAsync(It.IsAny<AnalysisFailedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AnalysisFailedEvent, CancellationToken>((e, _) => failed = e)
            .Returns(Task.CompletedTask);

        var cmd = new AnalyzeDocumentCommand(commandId, docId, "invoice.pdf");

        await CreateSut(okWriter, okPublisher).HandleAsync(cmd, CancellationToken.None);
        await CreateSut(failWriter, failPublisher).HandleAsync(cmd, CancellationToken.None);

        Assert.NotEqual(completed!.EventId, failed!.EventId);
    }

    [Fact]
    public async Task Successful_analysis_publishes_completed_event()
    {
        var docId = Guid.NewGuid();
        var blobWriter = new Mock<IBlobWriter>();
        var publisher = new Mock<IAnalysisResultEventPublisher>();

        blobWriter
            .Setup(b => b.SaveAsync(docId, It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("blob://analysis-results/result.json");

        var sut = CreateSut(blobWriter, publisher);

        await sut.HandleAsync(new AnalyzeDocumentCommand(Guid.NewGuid(), docId, "invoice.pdf"), CancellationToken.None);

        publisher.Verify(p => p.PublishAsync(
            It.Is<AnalysisCompletedEvent>(e =>
                e.DocumentId == docId &&
                e.BlobReference == "blob://analysis-results/result.json"),
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
        var blobWriter = new Mock<IBlobWriter>();
        var publisher = new Mock<IAnalysisResultEventPublisher>();

        blobWriter
            .Setup(b => b.SaveAsync(docId, It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("blob store unavailable"));

        var sut = CreateSut(blobWriter, publisher);

        await sut.HandleAsync(new AnalyzeDocumentCommand(Guid.NewGuid(), docId, "invoice.pdf"), CancellationToken.None);

        publisher.Verify(p => p.PublishAsync(
            It.Is<AnalysisFailedEvent>(e =>
                e.DocumentId == docId &&
                e.Reason.Contains("blob store unavailable")),
            It.IsAny<CancellationToken>()),
            Times.Once);

        publisher.Verify(p => p.PublishAsync(
            It.IsAny<AnalysisCompletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Cancellation_is_not_reported_as_an_analysis_failure()
    {
        // Shutting down mid-message is not an outcome - the message should be redelivered.
        var docId = Guid.NewGuid();
        var blobWriter = new Mock<IBlobWriter>();
        var publisher = new Mock<IAnalysisResultEventPublisher>();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        blobWriter
            .Setup(b => b.SaveAsync(docId, It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var sut = CreateSut(blobWriter, publisher);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => sut.HandleAsync(new AnalyzeDocumentCommand(Guid.NewGuid(), docId, "invoice.pdf"), cts.Token));

        publisher.Verify(p => p.PublishAsync(
            It.IsAny<AnalysisFailedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
