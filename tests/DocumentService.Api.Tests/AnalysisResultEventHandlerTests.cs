using DocumentIntelligence.Contracts;
using DocumentService.Api.Domain;
using DocumentService.Api.Infrastructure.Repositories;
using DocumentService.Api.Features.Documents.RecordAnalysisResult;
using Microsoft.Extensions.Logging;
using Moq;

// The events carry outcomes; these tests pin down that this service - not the worker -
// decides which lifecycle state each outcome maps to, and that the EventId reaches the
// guard that makes redelivery harmless.
public class AnalysisResultEventHandlerTests
{
    [Fact]
    public async Task Completed_event_marks_the_document_analyzed()
    {
        var repo = new Mock<IDocumentRepository>();
        repo.Setup(r => r.TryApplyAnalysisResultAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DocumentStatus>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new AnalysisCompletedEventHandler(
            repo.Object, Mock.Of<ILogger<AnalysisCompletedEventHandler>>());

        var eventId = Guid.NewGuid();
        var docId = Guid.NewGuid();

        await sut.HandleAsync(
            new AnalysisCompletedEvent(eventId, docId, "a summary", "blob://ref.json"),
            CancellationToken.None);

        repo.Verify(r => r.TryApplyAnalysisResultAsync(
            eventId,
            docId,
            "a summary",
            "blob://ref.json",
            DocumentStatus.Analyzed,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Failed_event_marks_the_document_failed()
    {
        var repo = new Mock<IDocumentRepository>();
        repo.Setup(r => r.TryApplyAnalysisFailureAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DocumentStatus>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new AnalysisFailedEventHandler(
            repo.Object, Mock.Of<ILogger<AnalysisFailedEventHandler>>());

        var eventId = Guid.NewGuid();
        var docId = Guid.NewGuid();

        await sut.HandleAsync(
            new AnalysisFailedEvent(eventId, docId, "blob store unavailable"),
            CancellationToken.None);

        repo.Verify(r => r.TryApplyAnalysisFailureAsync(
            eventId, docId, DocumentStatus.Failed, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_skipped_duplicate_is_not_an_error()
    {
        // The guard returning false means "already applied". The handler must complete
        // normally - throwing would abandon the message and cause endless redelivery.
        var repo = new Mock<IDocumentRepository>();
        repo.Setup(r => r.TryApplyAnalysisResultAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DocumentStatus>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = new AnalysisCompletedEventHandler(
            repo.Object, Mock.Of<ILogger<AnalysisCompletedEventHandler>>());

        var ex = await Record.ExceptionAsync(() => sut.HandleAsync(
            new AnalysisCompletedEvent(Guid.NewGuid(), Guid.NewGuid(), "s", "b"),
            CancellationToken.None));

        Assert.Null(ex);
    }
}
