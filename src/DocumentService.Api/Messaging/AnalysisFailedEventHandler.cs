using DocumentIntelligence.Contracts.Contracts;
using DocumentIntelligence.Contracts.DomainContracts;
using DocumentIntelligence.Contracts.Messaging;
using DocumentService.Api.Infrastructure.Repositories;

namespace DocumentService.Api.Messaging;

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

        var updated = await _repo.SetStatusAsync(evt.DocumentId, DocumentStatus.Failed, ct);

        if (!updated)
            _logger.LogWarning("Document {DocumentId} not found while applying failure.", evt.DocumentId);
    }
}
