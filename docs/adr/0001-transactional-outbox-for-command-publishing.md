# 0001 — Transactional outbox for command publishing

**Status:** Accepted · 2026-07-31

## Context

`RequestAnalysisController` committed a status change to the database and then published
`AnalyzeDocumentCommand` to Service Bus. Two writes, two systems, no shared transaction:

```csharp
await _repo.SetStatusAsync(documentId, DocumentStatus.Analyzing, ct);  // commits
await _publisher.PublishAsync(command, ct);                            // separate system
```

If the process died in between, or Service Bus was unreachable and the retries exhausted,
the document was left `Analyzing` with no command ever sent. No worker would touch it, and
nothing in the system would notice. That is the same liveness failure we fixed for the
*analysis-threw* case, reappearing at a different point in the pipeline.

The obvious fix — commit both atomically — is not available. A database and a broker are
independent transactional resources, and committing across them needs a distributed
transaction (2PC/XA). Azure Service Bus cannot enlist in a SQL transaction, and cloud
services deliberately do not offer 2PC: a coordinator holding locks across network calls
is slow, doesn't scale, and turns one stalled process into two stalled systems.

## Decision

Write the message into the database, in the same transaction as the state change that
justifies it, and let a background relay publish it afterwards.

This removes the second transactional resource rather than trying to coordinate it. There
is now exactly one commit at request time; publishing is a separate concern that can fail
and be retried without any state being lost.

- `OutboxMessages` table, written by `TryStartAnalysisAsync` in the same `SaveChanges` as
  the status change
- `OutboxDrainer` performs one pass — publish pending, record what succeeded
- `OutboxRelay` (a `BackgroundService`) schedules those passes

## Consequences

**Publishing becomes at-least-once.** The relay can publish successfully and then fail
before writing `SentAtUtc`, so it publishes again on the next pass. Sending and recording
the send are themselves two systems — the outbox contains that problem, it does not
abolish it. Consumers must be idempotent. The API's consumers already are (`ProcessedMessages`
inbox); **the worker's command consumer is not**, which is now a real gap rather than a
theoretical one.

**Publishing is no longer synchronous with the request.** A caller receiving `202 Accepted`
knows the command is durably queued, not that it has reached the broker. Worst case the
command is delayed by one poll interval (default 5s).

**A stalled relay is a silent failure.** If the relay dies, documents accumulate in
`Analyzing` and nothing publishes. The loop deliberately never exits on error, but that is
not monitoring — pending-message age is the metric that would need alerting in a real
deployment.

**Ordering is best-effort.** Messages are drained oldest-first, but a failed message does
not block later ones, so a persistent failure reorders relative to its successors. The
pipeline has no ordering requirement today.

**The outbox table grows.** Sent rows are never removed. Needs a sweep, same as the inbox.

## Alternatives considered

**Publish first, then commit.** Turns a lost command into a command for a document in the
wrong state — trading a stuck document for a phantom one. Not better.

**Reconciliation sweep instead of an outbox.** A job that finds documents stuck in
`Analyzing` and republishes. Cheaper, and it catches *every* cause of stuckness rather
than this one. It remains worth adding as a backstop — the outbox fixes a specific cause,
a sweep fixes the symptom regardless of cause. Not chosen as the primary mechanism because
it is detection-and-repair rather than prevention.

**Adopt MassTransit or NServiceBus.** Both ship a transactional outbox and inbox that hook
into the EF Core transaction, and in a product this is what we would use — very few teams
should hand-roll this. Rejected here because the repo's purpose is learning the mechanism,
and building it once is what makes the library's one line of configuration legible later.
