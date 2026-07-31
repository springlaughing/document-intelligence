using System.Diagnostics;
using DocumentIntelligence.Messaging;
using DocumentService.Api.Infrastructure.Ef;
using Microsoft.EntityFrameworkCore;

namespace DocumentService.Api.Infrastructure.Outbox;

// One pass over the outbox: publish what is pending, record what succeeded.
//
// Separate from OutboxPoller so that "what a pass does" can be tested without starting a
// background service and waiting on a timer. The poller owns scheduling; this owns work.
public class OutboxDrainer
{
    private readonly DocumentApiDbContext _db;
    private readonly IMessagePublisher _publisher;
    private readonly ILogger<OutboxDrainer> _logger;

    public OutboxDrainer(
        DocumentApiDbContext db,
        IMessagePublisher publisher,
        ILogger<OutboxDrainer> logger)
    {
        _db = db;
        _publisher = publisher;
        _logger = logger;
    }

    /// Returns the number of messages published in this pass.
    public async Task<int> DrainAsync(int batchSize, CancellationToken ct = default)
    {
        var pending = await _db.OutboxMessages
            .Where(m => m.SentAtUtc == null)
            .OrderBy(m => m.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

        if (pending.Count == 0) return 0;

        var sent = 0;

        foreach (var message in pending)
        {
            // Re-enter the trace of the request that queued this message, so the send
            // span the Azure SDK is about to emit hangs off the original operation
            // rather than starting a new, orphaned trace.
            using var activity = OutboxTelemetry.Source.StartActivity(
                "outbox publish", ActivityKind.Producer, message.TraceParent);

            activity?.SetTag("messaging.system", "servicebus");
            activity?.SetTag("messaging.destination.name", message.EntityName);
            activity?.SetTag("messaging.message.id", message.Id);
            activity?.SetTag("messaging.message.type", message.MessageType);

            try
            {
                // The outbox row id doubles as the broker MessageId. If this
                // publishes and then dies before recording the send, the retry carries
                // the same id - so an entity with duplicate detection turned on discards
                // it. Belt to the inbox's braces.
                await _publisher.PublishRawAsync(
                    message.EntityName, message.Payload, message.MessageType,
                    message.Id.ToString(), ct);

                // Publishing and recording the publish are two systems again, so they
                // cannot be atomic. Crashing here means the message is sent but still
                // marked pending, and the next pass sends it again - which is precisely
                // why this is at-least-once and consumers have to be idempotent.
                message.SentAtUtc = DateTimeOffset.UtcNow;
                message.LastError = null;
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Leave SentAtUtc null so the next pass retries. Recording the attempt is
                // what makes a permanently failing message visible rather than silently
                // looping forever.
                message.Attempts++;
                message.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;

                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

                _logger.LogError(ex,
                    "Failed to publish outbox message {MessageId} ({MessageType}); attempt {Attempts}.",
                    message.Id, message.MessageType, message.Attempts);
            }
        }

        await _db.SaveChangesAsync(ct);
        return sent;
    }
}
