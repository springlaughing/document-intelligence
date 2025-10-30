using System.Threading;
using System.Threading.Tasks;
using DocumentIntelligence.Contracts.Contracts;
using DocumentIntelligence.Contracts.DomainContracts;
using DocumentService.Api.Dtos;
using DocumentService.Api.Infrastructure.Repositories;
using DocumentService.Api.Messaging; 
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DocumentService.Api.Controllers;

[ApiController]
[Route("api/documents")]
[Produces("application/json")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentRepository _repo;
    private readonly IAnalyzeDocumentCommandPublisher _publisher;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        IDocumentRepository repo,
        IAnalyzeDocumentCommandPublisher publisher,
        ILogger<DocumentsController> logger)
    {
        _repo = repo;
        _publisher = publisher;
        _logger = logger;
    }

    // ✅ GET /api/documents/{documentId}
    // Readers (role user/admin or scope read)
    [HttpGet("{documentId:guid}")]
    [Authorize(Policy = "ReadAccess")]
    [ProducesResponseType(typeof(GetDocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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

    // ✅ POST /api/documents/{documentId}/analyze
    // Writers (role admin or scope write)
    // Body is OPTIONAL now; if FileName is not provided, we try to load it from the repo.
    [HttpPost("{documentId:guid}/analyze")]
    [Authorize(Policy = "WriteAccess")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> AnalyzeDocument(
        Guid documentId,
        CancellationToken ct)
    {

        // If no filename provided, try to load it; if not found, 404 or 400?
        // Here: if not found at all, 404. If found but filename empty => 400.
        var record = await _repo.GetAsync(documentId, ct);
        if (record is null)
        {
            // if user didn’t send filename and document doesn’t exist => not found
            return NotFound(new { message = "Document not found", documentId });
        }


        if (string.IsNullOrWhiteSpace(record.FileName))
        {
            return BadRequest(new
            {
                message = "FileName is required, please check the document.",
                documentId
            });
        }

        // Move to Analyzing (repo should be idempotent)
        await _repo.SetStatusAsync(documentId, DocumentStatus.Analyzing, ct);

        var command = new AnalyzeDocumentCommand(
            DocumentId: documentId,
            FileName: record.FileName
        );

        await _publisher.PublishAsync(command, ct);

        _logger.LogInformation(
            "Triggered analysis for document {DocumentId} ({FileName}) by {User}",
            documentId,
            record.FileName,
            User.Identity?.Name ?? "unknown"
        );

        return AcceptedAtAction(nameof(GetDocument), new { documentId }, new { documentId });
    }

    // ✅ POST /api/documents
    // Writers (admin or scope write)
    [HttpPost]
    [Authorize(Policy = "WriteAccess")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> UploadDocument(
        [FromBody] UploadDocumentDto request,
        CancellationToken ct)
    {
        var existed = await _repo.ExistsAsync(request.DocumentId, ct);
        await _repo.CreateIfNotExistsAsync(request.DocumentId, request.FileName, ct);

        if (existed)
        {
            _logger.LogInformation("Document already existed {DocumentId} ({FileName})",
                request.DocumentId, request.FileName);
            return Ok(new { documentId = request.DocumentId });
        }

        _logger.LogInformation("Registered new document {DocumentId} ({FileName})",
            request.DocumentId, request.FileName);

        return CreatedAtAction(nameof(GetDocument),
            new { documentId = request.DocumentId },
            new { documentId = request.DocumentId });
    }
}
