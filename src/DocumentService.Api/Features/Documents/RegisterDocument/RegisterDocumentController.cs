using DocumentService.Api.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocumentService.Api.Features.Documents.RegisterDocument;

[ApiController]
[Produces("application/json")]
public class RegisterDocumentController : ControllerBase
{
    private readonly IDocumentRepository _repo;
    private readonly ILogger<RegisterDocumentController> _logger;

    public RegisterDocumentController(
        IDocumentRepository repo,
        ILogger<RegisterDocumentController> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    // POST /api/documents
    // Writers (admin or scope write)
    [HttpPost("api/documents")]
    [Authorize(Policy = "WriteAccess")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> UploadDocument([FromBody] UploadDocumentDto request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.FileName))
            return BadRequest(Problem(title: "FileName is required.", detail: "Provide a non-empty file name."));

        var created = await _repo.CreateIfNotExistsAsync(request.DocumentId, request.FileName, ct);

        if (created)
        {
            _logger.LogInformation("Registered new document {DocumentId} ({FileName})", request.DocumentId, request.FileName);
            return CreatedAtRoute(
                DocumentRoutes.GetDocumentById,
                new { documentId = request.DocumentId },
                new { documentId = request.DocumentId });
        }

        _logger.LogInformation("Document already existed {DocumentId} ({FileName})", request.DocumentId, request.FileName);
        return Ok(new { documentId = request.DocumentId });
    }
}
