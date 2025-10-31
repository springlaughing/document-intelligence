using System.Threading;
using System.Threading.Tasks;
using DocumentIntelligence.Contracts.Contracts;
using DocumentIntelligence.Contracts.DomainContracts;
using DocumentService.Api.Controllers;
using DocumentService.Api.Infrastructure.Repositories;
using DocumentService.Api.Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;



public class DocumentsController_AnalyzeTests
{
    [Fact]
public async Task Analyze_publishes_command_with_correct_payload()
{
    var repo = new Mock<IDocumentRepository>();
    var publisher = new Mock<IAnalyzeDocumentCommandPublisher>();
    var logger = Mock.Of<ILogger<DocumentsController>>();

    var docId = Guid.NewGuid();

    repo.Setup(r => r.GetAsync(docId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new DocumentRecord(
            Id: docId,
            FileName: "invoice0.pdf",
            Status: DocumentStatus.Uploaded,
            AnalysisSummary: null,
            AnalysisBlobRef: null));

    repo.Setup(r => r.SetStatusAsync(docId, DocumentStatus.Analyzing, It.IsAny<CancellationToken>()))
        .ReturnsAsync(true);

    var sut = new DocumentsController(repo.Object, publisher.Object, logger);

    // ⬇️ Provide HttpContext and a user so ControllerBase.User is safe
    var httpContext = new DefaultHttpContext
    {
        User = new ClaimsPrincipal(
            new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "test-user") }, "TestAuth"))
    };
    sut.ControllerContext = new ControllerContext { HttpContext = httpContext };

    var result = await sut.AnalyzeDocument(docId, CancellationToken.None);

    var accepted = Assert.IsType<AcceptedAtActionResult>(result);
    Assert.Equal(nameof(DocumentsController.GetDocument), accepted.ActionName);

    publisher.Verify(p => p.PublishAsync(
        It.Is<AnalyzeDocumentCommand>(c => c.DocumentId == docId && c.FileName == "invoice0.pdf"),
        It.IsAny<CancellationToken>()),
        Times.Once);
}
}
