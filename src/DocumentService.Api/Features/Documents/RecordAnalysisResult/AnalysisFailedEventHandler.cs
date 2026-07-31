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
        var applied = await _repo.TryApplyAnalysisFailureAsync(
            eventId: evt.EventId,
            documentId: evt.DocumentId,
            status: DocumentStatus.Failed,
            ct: ct);

        if (!applied)
            _logger.LogInformation(
                "AnalysisFailedEvent {EventId} had no effect for {DocumentId}.", evt.EventId, evt.DocumentId);
    }
}
