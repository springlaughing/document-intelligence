using System.Threading;
using System.Threading.Tasks;
using DocumentService.Api.Features.Documents;
using DocumentService.Api.Features.Documents.RegisterDocument;
using DocumentService.Api.Infrastructure.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

public class RegisterDocumentControllerTests
{
    [Fact]
    public async Task Post_registers_new_document_when_created_returns_201()
    {
        var repo = new Mock<IDocumentRepository>();
        var logger = Mock.Of<ILogger<RegisterDocumentController>>();

        var docId = Guid.NewGuid();
        repo.Setup(r => r.CreateIfNotExistsAsync(docId, "invoice.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // <-- created

        var sut = new RegisterDocumentController(repo.Object, logger);

        var req = new UploadDocumentDto(docId, "invoice.pdf");
        var result = await sut.UploadDocument(req, CancellationToken.None);

        var created = Assert.IsType<CreatedAtRouteResult>(result);
        Assert.Equal(DocumentRoutes.GetDocumentById, created.RouteName);
        repo.Verify(r => r.CreateIfNotExistsAsync(docId, "invoice.pdf", It.IsAny<CancellationToken>()), Times.Once);
        repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Post_is_idempotent_when_already_exists_returns_200()
    {
        var repo = new Mock<IDocumentRepository>();
        var logger = Mock.Of<ILogger<RegisterDocumentController>>();

        var docId = Guid.NewGuid();
        repo.Setup(r => r.CreateIfNotExistsAsync(docId, "invoice.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // <-- already existed

        var sut = new RegisterDocumentController(repo.Object, logger);

        var result = await sut.UploadDocument(new UploadDocumentDto(docId, "invoice.pdf"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        repo.Verify(r => r.CreateIfNotExistsAsync(docId, "invoice.pdf", It.IsAny<CancellationToken>()), Times.Once);
        repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Post_returns_400_when_fileName_missing()
    {
        var repo = new Mock<IDocumentRepository>();
        var logger = Mock.Of<ILogger<RegisterDocumentController>>();

        var sut = new RegisterDocumentController(repo.Object, logger);

        var result = await sut.UploadDocument(new UploadDocumentDto(Guid.NewGuid(), ""), CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);

        repo.VerifyNoOtherCalls();
    }
}
