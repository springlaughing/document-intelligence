using DocumentIntelligence.Contracts;
using DocumentIntelligence.Messaging;
using DocumentService.Api.Domain;
using DocumentService.Api.Infrastructure.Repositories;

namespace DocumentService.Api.Features.Documents.RecordAnalysisResult;


public class AnalysisCompletedEventHandler : IMessageHandler<AnalysisCompletedEvent>
{
    private readonly IDocumentRepository _repo;
    private readonly ILogger<AnalysisCompletedEventHandler> _logger;

    public AnalysisCompletedEventHandler(
        IDocumentRepository repo,
        ILogger<AnalysisCompletedEventHandler> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task HandleAsync(AnalysisCompletedEvent evt, CancellationToken ct)
    {
        if (evt is null)
            throw new ArgumentNullException(nameof(evt));

        _logger.LogInformation("Applying AnalysisCompletedEvent for {DocumentId}", evt.DocumentId);

        // The event reports an outcome; this service owns the lifecycle and decides
        // which status that outcome maps to. Applying it is guarded by EventId, because
        // the broker guarantees at-least-once delivery, not exactly-once.
        var applied = await _repo.TryApplyAnalysisResultAsync(
            eventId: evt.EventId,
            documentId: evt.DocumentId,
            summary: evt.Summary,
            blobReference: evt.BlobReference,
            status: DocumentStatus.Analyzed,
            ct: ct
        );

        if (applied)
            _logger.LogInformation("Updated analysis result for {DocumentId}", evt.DocumentId);
        else
            _logger.LogInformation(
                "AnalysisCompletedEvent {EventId} had no effect for {DocumentId}.", evt.EventId, evt.DocumentId);
    }
}
