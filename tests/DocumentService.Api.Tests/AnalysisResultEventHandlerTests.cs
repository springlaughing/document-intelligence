using DocumentIntelligence.Contracts;
using DocumentIntelligence.Messaging;
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
        repo.Setup(r => r.ApplyAnalysisResultAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DocumentStatus>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplyOutcome.Applied);

        var sut = new AnalysisCompletedEventHandler(
            repo.Object, Mock.Of<ILogger<AnalysisCompletedEventHandler>>());

        var eventId = Guid.NewGuid();
        var docId = Guid.NewGuid();

        await sut.HandleAsync(
            new AnalysisCompletedEvent(eventId, docId, "a summary", "blob://ref.json"),
            CancellationToken.None);

        repo.Verify(r => r.ApplyAnalysisResultAsync(
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
        repo.Setup(r => r.ApplyAnalysisFailureAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DocumentStatus>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplyOutcome.Applied);

        var sut = new AnalysisFailedEventHandler(
            repo.Object, Mock.Of<ILogger<AnalysisFailedEventHandler>>());

        var eventId = Guid.NewGuid();
        var docId = Guid.NewGuid();

        await sut.HandleAsync(
            new AnalysisFailedEvent(eventId, docId, "blob store unavailable"),
            CancellationToken.None);

        repo.Verify(r => r.ApplyAnalysisFailureAsync(
            eventId, docId, DocumentStatus.Failed, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_skipped_duplicate_is_not_an_error()
    {
        // A duplicate is routine. The handler must complete normally - throwing would
        // abandon the message and cause endless redelivery of something already done.
        var repo = new Mock<IDocumentRepository>();
        repo.Setup(r => r.ApplyAnalysisResultAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DocumentStatus>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplyOutcome.AlreadyApplied);

        var sut = new AnalysisCompletedEventHandler(
            repo.Object, Mock.Of<ILogger<AnalysisCompletedEventHandler>>());

        var ex = await Record.ExceptionAsync(() => sut.HandleAsync(
            new AnalysisCompletedEvent(Guid.NewGuid(), Guid.NewGuid(), "s", "b"),
            CancellationToken.None));

        Assert.Null(ex);
    }

    [Fact]
    public async Task An_event_for_an_unknown_document_is_not_silently_discarded()
    {
        // Nothing can delete a document in this service, so this can only mean a defect,
        // crossed environments or lost data. Completing the message would throw the
        // evidence away; UnprocessableMessageException dead-letters it for inspection
        // without burning retries first.
        var repo = new Mock<IDocumentRepository>();
        repo.Setup(r => r.ApplyAnalysisResultAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DocumentStatus>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplyOutcome.DocumentNotFound);

        var sut = new AnalysisCompletedEventHandler(
            repo.Object, Mock.Of<ILogger<AnalysisCompletedEventHandler>>());

        var docId = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<UnprocessableMessageException>(() => sut.HandleAsync(
            new AnalysisCompletedEvent(Guid.NewGuid(), docId, "s", "b"),
            CancellationToken.None));

        Assert.Contains(docId.ToString(), ex.Message);
    }

    [Fact]
    public async Task A_failure_event_for_an_unknown_document_is_not_silently_discarded()
    {
        var repo = new Mock<IDocumentRepository>();
        repo.Setup(r => r.ApplyAnalysisFailureAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DocumentStatus>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplyOutcome.DocumentNotFound);

        var sut = new AnalysisFailedEventHandler(
            repo.Object, Mock.Of<ILogger<AnalysisFailedEventHandler>>());

        await Assert.ThrowsAsync<UnprocessableMessageException>(() => sut.HandleAsync(
            new AnalysisFailedEvent(Guid.NewGuid(), Guid.NewGuid(), "reason"),
            CancellationToken.None));
    }
}
