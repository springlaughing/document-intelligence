using DocumentIntelligence.Contracts.Contracts;
using DocumentIntelligence.Contracts.DomainContracts;
using Microsoft.EntityFrameworkCore;
using DocumentService.Api.Infrastructure.Repositories;


namespace DocumentService.Api.Messaging;
public class AnalysisCompletedEventHandler
{
    private readonly IDocumentRepository _repo;

    public AnalysisCompletedEventHandler(IDocumentRepository repo)
    {
        _repo = repo;
    }

    public async Task HandleAsync(AnalysisCompletedEvent evt, CancellationToken ct)
        {
            await _repo.UpdateAnalysisResultAsync(
                evt.DocumentId,
                evt.Summary,
                evt.BlobReference,
                evt.Status,
                ct: ct);
        }
}
