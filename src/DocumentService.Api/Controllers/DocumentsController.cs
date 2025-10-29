using DocumentIntelligence.Contracts.Contracts;
using DocumentIntelligence.Contracts.DomainContracts;
using DocumentIntelligence.Contracts.Messaging;
using DocumentService.Api.Dtos;
using DocumentService.Api.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocumentService.Api.Controllers;

[ApiController]
[Route("api/documents")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentRepository _repo;
    private readonly IMessageBus _bus;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        IDocumentRepository repo,
        IMessageBus bus,
        ILogger<DocumentsController> logger)
    {
        _repo = repo;
        _bus = bus;
        _logger = logger;
    }

    // ✅ GET /api/documents/{documentId}
    // Anyone authenticated can read (admin or user)
    [HttpGet("{documentId:guid}")]
    [Authorize(Roles = "admin,user")]
    [ProducesResponseType(typeof(GetDocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetDocumentDto>> GetDocument(
        Guid documentId,
        CancellationToken ct)
    {
        var record = await _repo.GetAsync(documentId, ct);

        if (record is null)
        {
            return NotFound(new
            {
                message = "Document not found",
                documentId
            });
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
    // Only admin can trigger background analysis
    [HttpPost("{documentId:guid}/analyze")]
    [Authorize(Roles = "admin")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> AnalyzeDocument(
        Guid documentId,
        [FromBody] AnalyzeDocumentDto request,
        CancellationToken ct)
    {
        // ensure doc exists (idempotent-ish create)
        await _repo.CreateIfNotExistsAsync(documentId, request.FileName, ct);

        // mark status -> Analyzing
        await _repo.SetStatusAsync(documentId, DocumentStatus.Analyzing, ct);

        // publish command to message bus
        var command = new AnalyzeDocumentCommand(
            DocumentId: documentId,
            FileName: request.FileName
        );

        await _bus.PublishAsync(command, ct);

        _logger.LogInformation(
            "Triggered analysis for document {DocumentId} ({FileName}) by {User}",
            documentId,
            request.FileName,
            User.Identity?.Name ?? "unknown"
        );

        return Accepted($"/api/documents/{documentId}");
    }

    // ✅ POST /api/documents
    // Admin can register/upload a new document record
    [HttpPost]
    [Authorize(Roles = "admin")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult> UploadDocument(
        [FromBody] UploadDocumentDto request,
        CancellationToken ct)
    {
        await _repo.CreateIfNotExistsAsync(request.DocumentId, request.FileName, ct);

        _logger.LogInformation(
            "Registered new document {DocumentId} ({FileName})",
            request.DocumentId,
            request.FileName
        );

        return CreatedAtAction(
            nameof(GetDocument),
            new { documentId = request.DocumentId },
            new { documentId = request.DocumentId }
        );
    }
}
