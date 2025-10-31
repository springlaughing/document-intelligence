// tests/DocumentService.Api.Tests/DocumentsController_RegisterTests.cs
using System.Threading;
using System.Threading.Tasks;
using DocumentService.Api.Controllers;
using DocumentService.Api.Dtos;
using DocumentService.Api.Infrastructure.Repositories;
using DocumentService.Api.Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

public class DocumentsController_RegisterTests
{
    [Fact]
    public async Task Post_registers_new_document_when_created_returns_201()
    {
        var repo = new Mock<IDocumentRepository>();
        var publisher = new Mock<IAnalyzeDocumentCommandPublisher>();
        var logger = Mock.Of<ILogger<DocumentsController>>();

        var docId = Guid.NewGuid();
        repo.Setup(r => r.CreateIfNotExistsAsync(docId, "invoice.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // <-- created

        var sut = new DocumentsController(repo.Object, publisher.Object, logger);

        var req = new UploadDocumentDto(docId, "invoice.pdf");
        var result = await sut.UploadDocument(req, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(DocumentsController.GetDocument), created.ActionName);
        repo.Verify(r => r.CreateIfNotExistsAsync(docId, "invoice.pdf", It.IsAny<CancellationToken>()), Times.Once);
        repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Post_is_idempotent_when_already_exists_returns_200()
    {
        var repo = new Mock<IDocumentRepository>();
        var publisher = new Mock<IAnalyzeDocumentCommandPublisher>();
        var logger = Mock.Of<ILogger<DocumentsController>>();

        var docId = Guid.NewGuid();
        repo.Setup(r => r.CreateIfNotExistsAsync(docId, "invoice.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // <-- already existed

        var sut = new DocumentsController(repo.Object, publisher.Object, logger);

        var result = await sut.UploadDocument(new UploadDocumentDto(docId, "invoice.pdf"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        repo.Verify(r => r.CreateIfNotExistsAsync(docId, "invoice.pdf", It.IsAny<CancellationToken>()), Times.Once);
        repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Post_returns_400_when_fileName_missing()
    {
        var repo = new Mock<IDocumentRepository>();
        var publisher = new Mock<IAnalyzeDocumentCommandPublisher>();
        var logger = Mock.Of<ILogger<DocumentsController>>();

        var sut = new DocumentsController(repo.Object, publisher.Object, logger);

        var result = await sut.UploadDocument(new UploadDocumentDto(Guid.NewGuid(), ""), CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);

        repo.VerifyNoOtherCalls();
    }
}
