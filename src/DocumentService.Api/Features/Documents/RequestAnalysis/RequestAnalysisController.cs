using DocumentIntelligence.Contracts;
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
    private readonly IAnalyzeDocumentCommandQueue _queue;
    private readonly ILogger<RequestAnalysisController> _logger;

    public RequestAnalysisController(
        IDocumentRepository repo,
        IAnalyzeDocumentCommandQueue queue,
        ILogger<RequestAnalysisController> logger)
    {
        _repo = repo;
        _queue = queue;
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

        var command = new AnalyzeDocumentCommand(
            DocumentId: documentId,
            FileName: record.FileName
        );

        // One call, one transaction: the document moves to Analyzing and the command is
        // queued together. Previously these were two writes to two systems, so dying in
        // between left the document Analyzing with no command ever sent, and nothing to
        // notice. The relay publishes from the outbox.
        var started = await _repo.TryStartAnalysisAsync(
            documentId, DocumentStatus.Analyzing, _queue.Prepare(command), ct);

        if (!started)
            return NotFound(new { message = "Document not found", documentId });

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
