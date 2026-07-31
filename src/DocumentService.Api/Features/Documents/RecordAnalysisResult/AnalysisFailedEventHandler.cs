using DocumentIntelligence.Contracts;
using DocumentIntelligence.Messaging;
using DocumentService.Api.Domain;
using DocumentService.Api.Infrastructure.Repositories;

namespace DocumentService.Api.Features.Documents.RecordAnalysisResult;

public class AnalysisFailedEventHandler : IMessageHandler<AnalysisFailedEvent>
{
    private readonly IDocumentRepository _repo;
    private readonly ILogger<AnalysisFailedEventHandler> _logger;

    public AnalysisFailedEventHandler(
        IDocumentRepository repo,
        ILogger<AnalysisFailedEventHandler> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task HandleAsync(AnalysisFailedEvent evt, CancellationToken ct)
    {
        if (evt is null)
            throw new ArgumentNullException(nameof(evt));

        _logger.LogWarning(
            "Applying AnalysisFailedEvent for {DocumentId}: {Reason}", evt.DocumentId, evt.Reason);

        // Guarded by EventId for the same reason as the completed handler: at-least-once.
        var outcome = await _repo.ApplyAnalysisFailureAsync(
            eventId: evt.EventId,
            documentId: evt.DocumentId,
            status: DocumentStatus.Failed,
            ct: ct);

        switch (outcome)
        {
            case ApplyOutcome.Applied:
                _logger.LogInformation("Marked {DocumentId} as failed.", evt.DocumentId);
                break;

            case ApplyOutcome.AlreadyApplied:
                _logger.LogInformation(
                    "AnalysisFailedEvent {EventId} was already applied to {DocumentId}.",
                    evt.EventId, evt.DocumentId);
                break;

            case ApplyOutcome.DocumentNotFound:
                throw new UnprocessableMessageException(
                    $"AnalysisFailedEvent {evt.EventId} refers to document {evt.DocumentId}, which does not exist.");
        }
    }
}
