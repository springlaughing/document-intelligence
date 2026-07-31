# 0002 — Accept the inbox re-check race rather than lock

**Status:** Accepted · 2026-07-31

## Context

`EfDocumentRepository.TryApplyOnceAsync` guards against duplicate delivery with an inbox
keyed `(MessageId, Handler)`, written in the same transaction as the change it guards.

Two concurrent deliveries of the same event do not collide on that key, which is what the
design originally assumed. They collide on the document's `RowVersion` first: the winner
commits and bumps it, so the loser's `UPDATE` matches zero rows and EF raises
`DbUpdateConcurrencyException` before its inbox insert is ever evaluated.

The handler therefore asks what it collided with — if this same event is already recorded,
the other delivery won and there is nothing to do; anything else is a genuine conflict with
a different writer and the message is retried.

That re-check can still be too early. If the winner has not committed at the moment the
loser looks, the loser sees no inbox row, treats it as a real conflict, and rethrows.

## Decision

Accept it. Do not lock, and do not add a retry loop around the re-check.

## Consequences

The loser's message is abandoned and redelivered. On redelivery the fast-path inbox check
finds the row and skips it. **The outcome is correct; the cost is one extra delivery cycle
in a narrow window.**

Closing it properly would mean pessimistic locking on the document row, or a
re-check-with-backoff loop inside the repository. Both add real complexity — lock
ordering, timeout tuning, a new class of stall — to a path that already converges on its
own. The trade is not worth it at this scale.

This is recorded rather than fixed because an accepted limitation that nobody wrote down
is indistinguishable from a bug nobody noticed. If the system later becomes latency
sensitive, or duplicate delivery becomes common rather than rare, revisit — the cost is
paid per duplicate, so it scales with how often duplicates actually happen.

## Related

- [0001](0001-transactional-outbox-for-command-publishing.md) makes duplicate delivery more
  likely, since outbox publishing is at-least-once. That raises the frequency of this path,
  though not its correctness.
