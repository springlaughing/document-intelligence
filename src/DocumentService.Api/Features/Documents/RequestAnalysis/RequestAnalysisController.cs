using DocumentIntelligence.Contracts.Contracts;
using DocumentService.Api.Domain;
using DocumentService.Api.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocumentService.Api.Features.Documents.RequestAnalysis;

[ApiController]
[Produces("application/json")]
public class RequestAnalysisController : ControllerBase
{
    private readonly IDocumentRepository _repo;
    private readonly IAnalyzeDocumentCommandPublisher _publisher;
    private readonly ILogger<RequestAnalysisController> _logger;

    public RequestAnalysisController(
        IDocumentRepository repo,
        IAnalyzeDocumentCommandPublisher publisher,
        ILogger<RequestAnalysisController> logger)
    {
        _repo = repo;
        _publisher = publisher;
        _logger = logger;
    }

    // POST /api/documents/{documentId}/analyze
    // Writers (role admin or scope write)
    [HttpPost("api/documents/{documentId:guid}/analyze")]
    [Authorize(Policy = "WriteAccess")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> AnalyzeDocument(
        Guid documentId,
        CancellationToken ct)
    {

        // If record not found at all, 404. If found but filename empty => 400.
        var record = await _repo.GetAsync(documentId, ct);
        if (record is null)
        {
            // if user didn't send filename and document doesn't exist => not found
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

        return AcceptedAtRoute(
            DocumentRoutes.GetDocumentById,
            new { documentId },
            new { documentId });
    }
}
