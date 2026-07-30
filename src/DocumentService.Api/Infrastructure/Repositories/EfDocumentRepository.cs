
using DocumentService.Api.Domain;
using DocumentService.Api.Infrastructure.Ef;
using Microsoft.EntityFrameworkCore;
using DocumentService.Api.Infrastructure.Ef.Entities;

namespace DocumentService.Api.Infrastructure.Repositories;
public class EfDocumentRepository : IDocumentRepository
{
    private readonly DocumentApiDbContext _db;
    //<EfDocumentRepository> part tells the logging system which category name to use for log messages coming from that class
    private readonly ILogger<EfDocumentRepository> _logger;

    public EfDocumentRepository(DocumentApiDbContext db, ILogger<EfDocumentRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<bool> ExistsAsync(Guid documentId, CancellationToken ct = default)
    {
        return await _db.Documents.AnyAsync(d => d.Id == documentId, ct);
    }

    public async Task<bool> CreateIfNotExistsAsync(Guid documentId, string fileName, CancellationToken ct = default)
{
    var entity = new DocumentEntity
    {
        Id = documentId,
        FileName = fileName,
        Status = DocumentStatus.Uploaded
    };

    _db.Documents.Add(entity);
    try
    {
        await _db.SaveChangesAsync(ct);
        return true; // created
    }
    catch (DbUpdateException ex) when (IsUniqueKeyViolation(ex))
    {
        _logger.LogDebug("Document {DocumentId} already existed during CreateIfNotExists.", documentId);
        return false; // already existed
    }
}



    public async Task<bool> SetStatusAsync(Guid documentId, DocumentStatus status, CancellationToken ct = default)
    {
        var entity = await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (entity is null) return false;

        entity.Status = status;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<DocumentRecord?> GetAsync(Guid documentId, CancellationToken ct = default)
    {
        var entity = await _db.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId, ct);

        if (entity is null) return null;

        return new DocumentRecord(
            entity.Id,
            entity.FileName,
            entity.Status,
            entity.AnalysisSummary,
            entity.AnalysisBlobRef
        );
    }

    public async Task UpdateAnalysisResultAsync(
        Guid documentId,
        string summary,
        string blobReference,
        DocumentStatus status,
        CancellationToken ct = default)
        {
            var entity = await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
            if (entity is null) return;

            entity.AnalysisSummary = summary;
            entity.AnalysisBlobRef = blobReference;
            entity.Status = status;

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(ex,
                    "Concurrency conflict updating analysis result for {DocumentId}",
                    documentId);

                // Depending on domain rules:
                // - swallow (best-effort update, okay if we lost the race)
                // - retry (re-read and reapply)
                // - or rethrow (bubble up as failure)
            }
        }

        private static bool IsUniqueKeyViolation(DbUpdateException ex)
        {
            return ex.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true
                || ex.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true;
        }
}
