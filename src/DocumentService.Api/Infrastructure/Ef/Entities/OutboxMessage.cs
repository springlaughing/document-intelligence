namespace DocumentService.Api.Infrastructure.Ef.Entities;

// A message this service intends to publish, stored in its own database.
//
// The point is to remove a dual write. Committing a state change and publishing to the
// broker are two separate systems, so they cannot succeed or fail together: die between
// them and the state says one thing while the broker never heard about it. Writing the
// message into the same transaction as the state change reduces that to a single commit
// - after which a poller is free to publish at its leisure, and to retry.
//
// The consequence is at-least-once publishing: the poller can send and then fail before
// recording that it sent, so it will send again. Consumers must be idempotent.
public class OutboxMessage
{
    public Guid Id { get; set; }

    // Queue or topic to publish to. Stored per message so the poller stays ignorant of
    // what any particular message means.
    public string EntityName { get; set; } = default!;

    // Contract type name, carried so the poller can set ServiceBusMessage.Subject
    // without deserializing the payload.
    public string MessageType { get; set; } = default!;

    public string Payload { get; set; } = default!;

    // W3C traceparent of the operation that queued this message. Stored because the
    // poller publishes later, on its own schedule, with no ambient trace of its own -
    // without this the outbox would silently cut every trace in half.
    public string? TraceParent { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    // Null until published. This is the column the poller scans.
    public DateTimeOffset? SentAtUtc { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }
}
