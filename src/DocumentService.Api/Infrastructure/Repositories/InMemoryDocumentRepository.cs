using System.Collections.Concurrent;
using DocumentIntelligence.Contracts.DomainContracts;

namespace DocumentService.Api.Infrastructure.Repositories;

public class InMemoryDocumentRepository : IDocumentRepository
{
    // pretend "database"
    private readonly ConcurrentDictionary<Guid, DocumentRecord> _store = new();

    public Task<bool> ExistsAsync(Guid documentId, CancellationToken ct = default)
    {
        return Task.FromResult(_store.ContainsKey(documentId));
    }

    public Task CreateIfNotExistsAsync(Guid documentId, string fileName, CancellationToken ct = default)
    {
        _store.AddOrUpdate(
            documentId,
            id => new DocumentRecord(
                Id: id,
                FileName: fileName,
                Status: DocumentStatus.Uploaded,
                AnalysisSummary: null,
                ExtractedEntities: null
            ),
            (id, existing) => existing // don't overwrite if exists
        );

        return Task.CompletedTask;
    }

    public Task SetStatusAsync(Guid documentId, DocumentStatus status, CancellationToken ct = default)
    {
        if (_store.TryGetValue(documentId, out var existing))
        {
            _store[documentId] = existing with { Status = status };
        }

        return Task.CompletedTask;
    }

    public Task<DocumentRecord?> GetAsync(Guid documentId, CancellationToken ct = default)
    {
        _store.TryGetValue(documentId, out var record);
        return Task.FromResult(record);
    }

    // (später werden wir hier noch ein Update reinbauen, wenn der Worker uns ein AnalysisResultDto zurückgeschickt hat)
    public Task ApplyAnalysisResultAsync(
        Guid documentId,
        string summary,
        string[] extractedEntities,
        CancellationToken ct = default)
    {
        if (_store.TryGetValue(documentId, out var existing))
        {
            _store[documentId] = existing with
            {
                Status = DocumentStatus.Analyzed,
                AnalysisSummary = summary,
                ExtractedEntities = extractedEntities
            };
        }

        return Task.CompletedTask;
    }
}
