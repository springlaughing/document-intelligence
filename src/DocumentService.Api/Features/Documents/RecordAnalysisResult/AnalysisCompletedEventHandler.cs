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
        var outcome = await _repo.ApplyAnalysisResultAsync(
            eventId: evt.EventId,
            documentId: evt.DocumentId,
            summary: evt.Summary,
            blobReference: evt.BlobReference,
            status: DocumentStatus.Analyzed,
            ct: ct
        );

        switch (outcome)
        {
            case ApplyOutcome.Applied:
                _logger.LogInformation("Updated analysis result for {DocumentId}", evt.DocumentId);
                break;

            case ApplyOutcome.AlreadyApplied:
                // Routine: the broker redelivered something already handled.
                _logger.LogInformation(
                    "AnalysisCompletedEvent {EventId} was already applied to {DocumentId}.",
                    evt.EventId, evt.DocumentId);
                break;

            case ApplyOutcome.DocumentNotFound:
                // Nothing can delete a document in this service, so this is a symptom -
                // a defect, crossed environments, or lost data. Retrying cannot help, and
                // completing the message would discard the evidence.
                throw new UnprocessableMessageException(
                    $"AnalysisCompletedEvent {evt.EventId} refers to document {evt.DocumentId}, which does not exist.");
        }
    }
}
