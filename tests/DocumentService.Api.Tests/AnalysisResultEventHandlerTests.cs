using DocumentIntelligence.Contracts;
using DocumentService.Api.Domain;
using DocumentService.Api.Infrastructure.Repositories;
using DocumentService.Api.Features.Documents.RecordAnalysisResult;
using Microsoft.Extensions.Logging;
using Moq;

// The events carry outcomes; these tests pin down that this service - not the worker -
// decides which lifecycle state each outcome maps to.
public class AnalysisResultEventHandlerTests
{
    [Fact]
    public async Task Completed_event_marks_the_document_analyzed()
    {
        var repo = new Mock<IDocumentRepository>();
        var sut = new AnalysisCompletedEventHandler(
            repo.Object, Mock.Of<ILogger<AnalysisCompletedEventHandler>>());

        var docId = Guid.NewGuid();

        await sut.HandleAsync(
            new AnalysisCompletedEvent(docId, "a summary", "blob://ref.json"),
            CancellationToken.None);

        repo.Verify(r => r.UpdateAnalysisResultAsync(
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
        repo.Setup(r => r.SetStatusAsync(
                It.IsAny<Guid>(), It.IsAny<DocumentStatus>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new AnalysisFailedEventHandler(
            repo.Object, Mock.Of<ILogger<AnalysisFailedEventHandler>>());

        var docId = Guid.NewGuid();

        await sut.HandleAsync(
            new AnalysisFailedEvent(docId, "blob store unavailable"),
            CancellationToken.None);

        repo.Verify(r => r.SetStatusAsync(
            docId, DocumentStatus.Failed, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
