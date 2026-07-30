using DocumentService.Api.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocumentService.Api.Features.Documents.GetDocument;

[ApiController]
[Produces("application/json")]
public class GetDocumentController : ControllerBase
{
    private readonly IDocumentRepository _repo;

    public GetDocumentController(IDocumentRepository repo)
    {
        _repo = repo;
    }

    // GET /api/documents/{documentId}
    // Readers (role user/admin or scope read)
    [HttpGet("api/documents/{documentId:guid}", Name = DocumentRoutes.GetDocumentById)]
    [Authorize(Policy = "ReadAccess")]
    [ProducesResponseType(typeof(GetDocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<GetDocumentDto>> GetDocument(Guid documentId, CancellationToken ct)
    {
        var record = await _repo.GetAsync(documentId, ct);
        if (record is null)
        {
            return NotFound(new { message = "Document not found", documentId });
        }

        var dto = new GetDocumentDto(
            DocumentId: record.Id,
            FileName: record.FileName,
            Status: record.Status.ToString(),
            AnalysisSummary: record.AnalysisSummary,
            AnalysisBlobRef: record.AnalysisBlobRef
        );

        return Ok(dto);
    }
}
