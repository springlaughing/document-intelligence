using DocumentIntelligence.Contracts.Contracts;
using DocumentIntelligence.Contracts.Messaging;
using DocumentService.Api.Infrastructure.Repositories;


namespace DocumentService.Api.Messaging;


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


        await _repo.UpdateAnalysisResultAsync(
            documentId: evt.DocumentId,
            summary: evt.Summary,
            blobReference: evt.BlobReference,
            status: evt.Status,
            ct: ct
        );

        _logger.LogInformation("Updated analysis result for {DocumentId}", evt.DocumentId);
    }
}
