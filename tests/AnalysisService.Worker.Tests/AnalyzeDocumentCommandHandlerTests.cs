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
    public async Task Successful_analysis_publishes_completed_event()
    {
        var docId = Guid.NewGuid();
        var blobWriter = new Mock<IBlobWriter>();
        var publisher = new Mock<IAnalysisResultEventPublisher>();

        blobWriter
            .Setup(b => b.SaveAsync(docId, It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("blob://analysis-results/result.json");

        var sut = CreateSut(blobWriter, publisher);

        await sut.HandleAsync(new AnalyzeDocumentCommand(docId, "invoice.pdf"), CancellationToken.None);

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

        await sut.HandleAsync(new AnalyzeDocumentCommand(docId, "invoice.pdf"), CancellationToken.None);

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
            () => sut.HandleAsync(new AnalyzeDocumentCommand(docId, "invoice.pdf"), cts.Token));

        publisher.Verify(p => p.PublishAsync(
            It.IsAny<AnalysisFailedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
