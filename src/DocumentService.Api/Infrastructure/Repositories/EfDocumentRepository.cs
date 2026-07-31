
using System.Diagnostics;
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

    // Inbox discriminators. Stable strings rather than nameof(), because renaming a
    // handler must not silently make every already-applied event look unseen.
    private const string AnalysisCompletedHandler = "AnalysisCompleted";
    private const string AnalysisFailedHandler = "AnalysisFailed";

    public Task<ApplyOutcome> ApplyAnalysisResultAsync(
        Guid eventId,
        Guid documentId,
        string summary,
        string blobReference,
        DocumentStatus status,
        CancellationToken ct = default) =>
        ApplyOnceAsync(eventId, AnalysisCompletedHandler, documentId, entity =>
        {
            entity.AnalysisSummary = summary;
            entity.AnalysisBlobRef = blobReference;
            entity.Status = status;
        }, ct);

    public Task<ApplyOutcome> ApplyAnalysisFailureAsync(
        Guid eventId,
        Guid documentId,
        DocumentStatus status,
        CancellationToken ct = default) =>
        ApplyOnceAsync(eventId, AnalysisFailedHandler, documentId,
            entity => entity.Status = status, ct);

    public async Task<bool> TryStartAnalysisAsync(
        Guid documentId,
        DocumentStatus status,
        OutboxEnqueue message,
        CancellationToken ct = default)
    {
        var entity = await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (entity is null) return false;

        entity.Status = status;

        _db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EntityName = message.EntityName,
            MessageType = message.MessageType,
            Payload = message.Payload,

            // Captured here, while still inside the request that decided to send this.
            TraceParent = Activity.Current?.Id,

            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        // The single commit that replaces "save the status, then publish". There is no
        // longer a second write that can fail on its own and strand the document.
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<ApplyOutcome> ApplyOnceAsync(
        Guid eventId,
        string handler,
        Guid documentId,
        Action<DocumentEntity> apply,
        CancellationToken ct)
    {
        // Fast path for the ordinary case: the broker redelivered something already done.
        if (await _db.ProcessedMessages.AnyAsync(
                m => m.MessageId == eventId && m.Handler == handler, ct))
        {
            _logger.LogInformation(
                "Event {EventId} was already applied by {Handler}; skipping.", eventId, handler);
            return ApplyOutcome.AlreadyApplied;
        }

        var entity = await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (entity is null)
        {
            _logger.LogWarning(
                "Document {DocumentId} not found while applying event {EventId}.", documentId, eventId);
            return ApplyOutcome.DocumentNotFound;
        }

        apply(entity);

        _db.ProcessedMessages.Add(new ProcessedMessage
        {
            MessageId = eventId,
            Handler = handler,
            ProcessedAtUtc = DateTimeOffset.UtcNow
        });

        try
        {
            // One SaveChanges, therefore one transaction. The document change and the
            // record that it happened cannot end up disagreeing.
            await _db.SaveChangesAsync(ct);
            return ApplyOutcome.Applied;
        }
        catch (DbUpdateException ex) when (IsUniqueKeyViolation(ex))
        {
            // Lost the race to a concurrent delivery of the same event. It applied; we
            // did not. The check above cannot prevent this, the key constraint can.
            _logger.LogInformation(
                "Concurrent delivery of {EventId} was applied by {Handler} first.", eventId, handler);
            return ApplyOutcome.AlreadyApplied;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Someone changed the document between our read and our write. Now that
            // RowVersion is a real concurrency token, this fires before the inbox key
            // ever gets a chance to - two concurrent deliveries of one event collide on
            // the document row first.
            //
            // So ask what the conflict was. If this same event is now recorded, the other
            // delivery won and there is nothing left to do. Anything else is a real
            // conflict with a different writer, and the message should be retried.
            _db.ChangeTracker.Clear();

            if (await _db.ProcessedMessages.AnyAsync(
                    m => m.MessageId == eventId && m.Handler == handler, ct))
            {
                _logger.LogInformation(
                    "Concurrent delivery of {EventId} was applied by {Handler} first.", eventId, handler);
                return ApplyOutcome.AlreadyApplied;
            }

            _logger.LogWarning(ex,
                "Concurrency conflict applying event {EventId} to document {DocumentId}.",
                eventId, documentId);
            throw;
        }
    }

        private static bool IsUniqueKeyViolation(DbUpdateException ex)
        {
            return ex.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true
                || ex.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true;
        }
}
