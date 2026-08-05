# 0004 — Reconcile stuck analyses with a level-triggered sweep

**Status:** Accepted · 2026-08-05

## Context

[0001](0001-transactional-outbox-for-command-publishing.md) guarantees that if a document
moves to `Analyzing`, the analyze command is eventually published.
[0003](0003-deterministic-event-ids-instead-of-a-worker-inbox.md) guarantees that a result
event is applied at most once. Between them they cover every message that exists.

Neither covers a message that never exists. 0003 says so directly, under "Known gap": if a
command exhausts `MaxDeliveryCount` and dead-letters, no event is ever published and the
document stays `Analyzing` with nothing detecting it.

This is not an implementation oversight, it is a property of the architecture. Every
component in this service is **edge-triggered** — it runs in response to a message
arriving. A handler that never receives a message never runs, so no handler can observe
that a document is stranded. *You cannot write a handler for the message you did not get.*

Detecting an absence requires reading state rather than reacting to events.

## Decision

Add a **level-triggered** reconciliation loop: a timer that reads documents in `Analyzing`
older than a threshold, queues the analyze command again through the existing outbox, and
after a configured number of attempts marks the document `Failed` with a reason and counts
it as a metric.

`StuckAnalysisScheduler` owns the timer, `StuckAnalysisReconciler` owns a pass — the same
split as `OutboxPoller`/`OutboxDrainer` and `CleanupScheduler`/`OldMessageCleaner`. It sits
in the `RequestAnalysis` slice because re-requesting analysis is what it does, and it goes
through `IDocumentRepository` like everything else outside `Infrastructure`.

Because it keys off state and not cause, it does not care *how* a document got stuck: a
dead-lettered command, a worker killed mid-analysis, a bug nobody has found. That
indifference is the point. It is the only component here that can catch a failure mode
nobody enumerated in advance.

## Alternatives considered

**Scheduled (delayed) Service Bus messages.** On entering `Analyzing`, also enqueue a
"check this document" message with `ScheduledEnqueueTime` set past the threshold. Neater
than a sweep: no table scan, per-document timer precision, cost proportional to work rather
than to table size. It also composes well with what already exists, since the schedule
would go out through the outbox and so be transactional with the status change.

Rejected because **it only fires for documents where the scheduling itself succeeded.** If
the fault is that a document reached `Analyzing` without a timer being written — a bug, a
crash between two writes, a code path added later that forgets — nothing fires, and the
document is stuck with nobody looking for it. That is the same class of failure it is meant
to protect against, so it is a second strand of the same net rather than a net under it.
Worth adding later *alongside* the sweep for latency; not instead of it.

**A durable workflow engine** (Temporal, Durable Functions, MassTransit/NServiceBus sagas).
Makes the timeout a declared part of a state machine, which is the correct answer at scale.
Rejected as disproportionate: it would reframe the entire pipeline around a large new
dependency to close one gap.

**Nothing — surface stuck documents in a dashboard and let a human re-trigger.** Honest, and
it needs someone watching. Rejected because the API already exposes everything needed to
re-trigger, so a human is only being asked to run a loop a computer can run.

## Consequences

**A re-queue uses a new `CommandId`.** The worker skips analysis it has already performed by
checking the output store for what that command id would have produced (0003), so reusing
the id risks the retry being recognised as already done and doing nothing — the exact
outcome this exists to prevent. The cost is that a document whose analysis genuinely
completed, and only lost its result event, is analysed a second time. Redundant work is the
cheaper mistake.

**The threshold is the dangerous knob.** Set below worst-case legitimate analysis time, the
sweep re-queues documents that were merely slow and manufactures the duplicate work the
inbox exists to absorb. It is measured from `AnalysisStartedAtUtc`, which is restamped on
every attempt, so a re-queued document gets a full fresh window.

**Attempts are counted with a compare-and-swap.** Every replica runs its own sweep, so two
can select the same document. Re-queued *sends* are covered by the broker's duplicate
detection, but the attempt increment is a plain database write that nothing else dedupes —
without a conditional update the limit would be reached at twice the intended rate.
`TryRetryAnalysisAsync` and `TryFailStuckAnalysisAsync` therefore take the attempt count the
caller observed and no-op if it has moved. On SQL Server `RowVersion` catches this too; the
explicit check also holds on the InMemory provider, which ignores concurrency tokens.

**Documents with a null `AnalysisStartedAtUtc` are never swept**, since re-queueing on an
unknown start time is a guess. The migration backfills existing `Analyzing` rows with the
deploy time rather than leaving them permanently invisible — costing one sweep window, and
avoiding a mass re-queue at the moment of deployment.

**The metrics matter more than the repair.** A loop that quietly fixes a rising number of
documents is indistinguishable from a loop with nothing to do, which is how a systemic
upstream break stays invisible for a month. `reconciliation.documents.requeued` trending
upward is an alert, not a success story.

**This closes the gap named in 0001 and 0003** and does not supersede either: the outbox
still removes the dual write, the inbox still absorbs duplicates. The sweep is the backstop
for what neither can see.

## Related

- [0001](0001-transactional-outbox-for-command-publishing.md) considered a reconciliation
  pass *instead of* an outbox and rejected it as the primary mechanism. This adopts it as
  the secondary one, which is the role that ADR left open.
- [0003](0003-deterministic-event-ids-instead-of-a-worker-inbox.md) names this gap and
  states that catching unenumerated failure modes is its real value.
