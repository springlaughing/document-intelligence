namespace DocumentService.Api.Infrastructure.Ef.Entities;

// One row per (message, handler) that has been applied - the inbox.
//
// The row is written in the same transaction as the change it guards, so "I have
// handled this event" and "here is the effect of handling it" commit together or not
// at all. Keeping them in one store is the whole point: a separate cache would put
// the record and its effect in different transactions and reintroduce a dual write.
public class ProcessedMessage
{
    public Guid MessageId { get; set; }

    // Which consumer applied it. The same event may legitimately be applied by more
    // than one handler, but never twice by the same one.
    public string Handler { get; set; } = default!;

    public DateTimeOffset ProcessedAtUtc { get; set; }
}
