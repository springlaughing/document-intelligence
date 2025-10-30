using DocumentIntelligence.Contracts.Contracts;
using DocumentService.Api.Infrastructure.Repositories;


namespace DocumentService.Api.Messaging;

public interface IAnalysisCompletedEventHandler
{
    Task HandleAsync(AnalysisCompletedEvent evt, CancellationToken ct);
}

public class AnalysisCompletedEventHandler : IAnalysisCompletedEventHandler
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

        // Optional: if your repo tracks processed events, short-circuit here to be idempotent
        // if (await _repo.HasProcessedEventAsync(evt.EventId, ct)) return;

        await _repo.UpdateAnalysisResultAsync(
            documentId: evt.DocumentId,
            summary: evt.Summary,
            blobReference: evt.BlobReference,
            status: evt.Status,
            ct: ct
        );

        // Optional: mark processed
        // await _repo.MarkEventProcessedAsync(evt.EventId, ct);

        _logger.LogInformation("Updated analysis result for {DocumentId}", evt.DocumentId);
    }
}
