using DocumentService.Api.Infrastructure.Ef;
using Microsoft.EntityFrameworkCore;

namespace DocumentService.Api.Infrastructure.Outbox;

// Deletes inbox and outbox rows that can no longer matter.
//
// Both tables are append-only in normal operation and would otherwise grow without
// limit. What makes deletion safe is message expiry: once a message has outlived the
// broker's DefaultMessageTimeToLive it can never be redelivered, so the inbox row that
// would have recognised it is dead weight. The retention window must stay comfortably
// longer than that TTL, or the guard is removed while duplicates are still possible.
public class OldMessageCleaner
{
    private readonly DocumentApiDbContext _db;
    private readonly ILogger<OldMessageCleaner> _logger;

    public OldMessageCleaner(DocumentApiDbContext db, ILogger<OldMessageCleaner> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// Returns how many rows were removed.
    public async Task<int> CleanAsync(TimeSpan retention, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - retention;

        var staleInbox = await _db.ProcessedMessages
            .Where(m => m.ProcessedAtUtc < cutoff)
            .ToListAsync(ct);

        // Only sent rows. An unsent one is still owed to the broker no matter how old,
        // and deleting it would lose the message the outbox exists to protect.
        var staleOutbox = await _db.OutboxMessages
            .Where(m => m.SentAtUtc != null && m.SentAtUtc < cutoff)
            .ToListAsync(ct);

        if (staleInbox.Count == 0 && staleOutbox.Count == 0) return 0;

        _db.ProcessedMessages.RemoveRange(staleInbox);
        _db.OutboxMessages.RemoveRange(staleOutbox);
        await _db.SaveChangesAsync(ct);

        var removed = staleInbox.Count + staleOutbox.Count;

        _logger.LogInformation(
            "Swept {Inbox} inbox and {Outbox} outbox rows older than {Cutoff:u}.",
            staleInbox.Count, staleOutbox.Count, cutoff);

        return removed;
    }
}
