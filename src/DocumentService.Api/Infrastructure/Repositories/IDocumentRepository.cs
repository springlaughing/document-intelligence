using DocumentIntelligence.Contracts.DomainContracts;

namespace DocumentService.Api.Infrastructure.Repositories;

public interface IDocumentRepository
{
    Task<bool> ExistsAsync(Guid documentId, CancellationToken ct = default);

    Task CreateIfNotExistsAsync(Guid documentId, string fileName, CancellationToken ct = default);

    Task<bool> SetStatusAsync(Guid documentId, DocumentStatus status, CancellationToken ct = default);

    Task<DocumentRecord?> GetAsync(Guid documentId, CancellationToken ct = default);
    Task UpdateAnalysisResultAsync(Guid documentId, string summary, string blobReference, DocumentStatus status = DocumentStatus.Analyzed, CancellationToken ct = default);

}

//internal projection type (not EF entity, not the HTTP DTO).
public record DocumentRecord(
    Guid Id,
    string FileName,
    DocumentStatus Status,
    string? AnalysisSummary,
    string? AnalysisBlobRef

    
);
