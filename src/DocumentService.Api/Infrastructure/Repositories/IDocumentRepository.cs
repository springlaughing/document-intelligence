using DocumentService.Api.Domain;

namespace DocumentService.Api.Infrastructure.Repositories;

public interface IDocumentRepository
{
    Task<bool> ExistsAsync(Guid documentId, CancellationToken ct = default);

    Task<bool> CreateIfNotExistsAsync(Guid documentId, string fileName, CancellationToken ct = default);

    Task<bool> SetStatusAsync(Guid documentId, DocumentStatus status, CancellationToken ct = default);

    Task<DocumentRecord?> GetAsync(Guid documentId, CancellationToken ct = default);
    // Applies an analysis outcome at most once per event.
    //
    // Service Bus delivers at least once, so these are the entry points that have to
    // tolerate seeing the same event twice. Returns false when the event has already
    // been applied, or when the document is unknown - in both cases nothing changed.
    //
    // Status is passed explicitly: mapping an analysis outcome onto a lifecycle state
    // is a decision for the caller, not a storage default.
    Task<bool> TryApplyAnalysisResultAsync(
        Guid eventId, Guid documentId, string summary, string blobReference,
        DocumentStatus status, CancellationToken ct = default);

    Task<bool> TryApplyAnalysisFailureAsync(
        Guid eventId, Guid documentId, DocumentStatus status, CancellationToken ct = default);

}

//internal projection type (not EF entity, not the HTTP DTO).
public record DocumentRecord(
    Guid Id,
    string FileName,
    DocumentStatus Status,
    string? AnalysisSummary,
    string? AnalysisBlobRef

    
);
