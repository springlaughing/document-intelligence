using DocumentIntelligence.Contracts.DomainContracts;

namespace DocumentService.Api.Infrastructure.Repositories;

public interface IDocumentRepository
{
    Task<bool> ExistsAsync(Guid documentId, CancellationToken ct = default);

    Task CreateIfNotExistsAsync(Guid documentId, string fileName, CancellationToken ct = default);

    Task SetStatusAsync(Guid documentId, DocumentStatus status, CancellationToken ct = default);

    Task<DocumentRecord?> GetAsync(Guid documentId, CancellationToken ct = default);
    Task ApplyAnalysisResultAsync(
        Guid documentId,
        string summary,
        string[] extractedEntities,
        CancellationToken ct = default);
}

// This is our internal representation of a "document" in the API service.
public record DocumentRecord(
    Guid Id,
    string FileName,
    DocumentStatus Status,
    string? AnalysisSummary,
    string[]? ExtractedEntities

    
);
