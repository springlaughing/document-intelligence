using System.Threading;
using System.Threading.Tasks;
using DocumentIntelligence.Contracts;
using DocumentService.Api.Domain;
using DocumentService.Api.Features.Documents;
using DocumentService.Api.Features.Documents.RequestAnalysis;
using DocumentService.Api.Infrastructure.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

public class RequestAnalysisControllerTests
{
    private static readonly OutboxEnqueue Prepared =
        new("analyze-document", nameof(AnalyzeDocumentCommand), "{}");

    private static RequestAnalysisController CreateSut(
        Mock<IDocumentRepository> repo,
        Mock<IAnalyzeDocumentCommandQueue> queue)
    {
        var sut = new RequestAnalysisController(
            repo.Object, queue.Object, Mock.Of<ILogger<RequestAnalysisController>>());

        // ControllerBase.User is read when logging who triggered the analysis.
        sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "test-user") }, "TestAuth"))
            }
        };

        return sut;
    }

    private static Mock<IDocumentRepository> RepoWithDocument(Guid docId, string fileName = "invoice0.pdf")
    {
        var repo = new Mock<IDocumentRepository>();
        repo.Setup(r => r.GetAsync(docId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentRecord(
                Id: docId,
                FileName: fileName,
                Status: DocumentStatus.Uploaded,
                AnalysisSummary: null,
                AnalysisBlobRef: null));
        return repo;
    }

    [Fact]
    public async Task Analyze_queues_the_command_in_the_same_call_that_sets_the_status()
    {
        var docId = Guid.NewGuid();
        var repo = RepoWithDocument(docId);
        var queue = new Mock<IAnalyzeDocumentCommandQueue>();

        queue.Setup(q => q.Prepare(It.IsAny<AnalyzeDocumentCommand>())).Returns(Prepared);
        repo.Setup(r => r.TryStartAnalysisAsync(
                docId, DocumentStatus.Analyzing, Prepared, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateSut(repo, queue).AnalyzeDocument(docId, CancellationToken.None);

        var accepted = Assert.IsType<AcceptedAtRouteResult>(result);
        Assert.Equal(DocumentRoutes.GetDocumentById, accepted.RouteName);

        queue.Verify(q => q.Prepare(
            It.Is<AnalyzeDocumentCommand>(c => c.DocumentId == docId && c.FileName == "invoice0.pdf")),
            Times.Once);

        // The command and the status change go together; nothing publishes separately.
        repo.Verify(r => r.TryStartAnalysisAsync(
            docId, DocumentStatus.Analyzing, Prepared, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Analyze_returns_404_when_the_document_vanished_before_the_write()
    {
        var docId = Guid.NewGuid();
        var repo = RepoWithDocument(docId);
        var queue = new Mock<IAnalyzeDocumentCommandQueue>();

        queue.Setup(q => q.Prepare(It.IsAny<AnalyzeDocumentCommand>())).Returns(Prepared);
        repo.Setup(r => r.TryStartAnalysisAsync(
                It.IsAny<Guid>(), It.IsAny<DocumentStatus>(), It.IsAny<OutboxEnqueue>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateSut(repo, queue).AnalyzeDocument(docId, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Analyze_returns_404_for_an_unknown_document()
    {
        var repo = new Mock<IDocumentRepository>();
        repo.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord?)null);

        var queue = new Mock<IAnalyzeDocumentCommandQueue>();

        var result = await CreateSut(repo, queue).AnalyzeDocument(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
        repo.Verify(r => r.TryStartAnalysisAsync(
            It.IsAny<Guid>(), It.IsAny<DocumentStatus>(), It.IsAny<OutboxEnqueue>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
