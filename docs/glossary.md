# Glossary

Terms used in this repo's code, comments and decision records. Each entry says what the
word means. Where a choice is argued rather than merely named, the entry links to the
record that argues it.

---

**At-least-once delivery**
The guarantee Azure Service Bus gives: every message is delivered at least one time, and
possibly more. It never silently loses one, but it may hand you the same message twice —
after a network problem, a timeout, or a consumer that died holding it. Everything that
consumes messages here has to cope with seeing one twice.

**Dead-letter**
A side queue the broker moves a message into when it cannot be delivered successfully —
after ten failed attempts, or when a handler declares it unprocessable. The message is not
deleted; it waits there for a person to look at it. Nothing in this system consumes
dead-lettered messages, which is part of why the sweep exists.

**Deterministic id**
An identifier calculated by hashing values you already have, rather than generated at
random, so the same inputs always produce the same id. The worker builds each event id from
two things: the **command id** it is responding to, and a **purpose string** —
`"analysis-completed"` or `"analysis-failed"`. The two are joined as `commandId:purpose` and
hashed with SHA-256, and the first 16 bytes become the id. The command id is what makes the
result repeatable across instances; the purpose string keeps the two possible outcomes of one
command from sharing an identity. So two workers handling the same command produce the
*same* event id, and the API's inbox recognises the second one as a duplicate.
See [ADR 0003](adr/0003-deterministic-event-ids-instead-of-a-worker-inbox.md).

**Drain**
To empty a queue or table by processing everything in it. `OutboxDrainer` performs one pass
over the outbox: publish what is pending, record what succeeded.

**Dual write**
Writing to two systems that cannot share a transaction — a group of changes that all take
effect together or not at all. Here the two systems are the document database where we need to write the document state, e.g. that the document is now in `Analyzing` state, and the
Service Bus broker where we suppose to post commands directly using PublishAsync function from the serbvice bus library.

Neither ordering is safe:

- **Publish command, then update document state to db.** The command goes on the queue. If
  `SaveChanges` then fails — a dropped connection, a timeout, a concurrency conflict — its
  transaction rolls back, and a command now exists for work the database has no record of.
- **Update the state in db, then publish a command to the broker.** The state commits. If the
  process then stops before the publish succeeds — a deployment restarting the container, an
  out-of-memory kill, or a broker outage that exhausts the retries — the document stays
  `Analyzing` for ever, no command was ever sent, and the only code that intended to send it
  is gone.

The outbox, a new table in our app's db, is solution to that problem. It enables one transaction/write to one system (our
db): `TryStartAnalysisAsync` sets the status in db AND inserts the message row in a single
`SaveChanges`, so both land or neither does.

This reliability comes at unavoidable cost and introduces a new mechanism that was not there before: `OutboxPoller`, a timed background service that polls/gets commands for the broker from our db each 5 seconds.

Each time it wakes it calls `OutboxDrainer.DrainAsync`, which for each pending row — meaning one whose `SentAtUtc` is still null, see **Outbox** — does two
things in order: sends the message to the broker with `PublishRawAsync`, then sets
`SentAtUtc` on that row and calls `SaveChangesAsync`. Those two are a dual write again —
same two systems, still no shared transaction.

What changed is the failure a crash between them produces. Suppose `PublishRawAsync` has
succeeded and the process dies before `SaveChangesAsync` runs. The assignment to `SentAtUtc`
existed only in memory, so the `OutboxMessages` row is untouched and still has
`SentAtUtc = null`. That is exactly what every drainer selects on
(`Where(m => m.SentAtUtc == null)`), so the next pass finds that row and publishes it again.
The next pass need not be in this process: if the crash took this replica down, another
replica's poller picks the row up on its next tick, because the row is in the shared
database rather than in anyone's memory.

The republish carries the same message id, since that id is the outbox row's own id, so the
broker's duplicate detection discards the copy. A duplicate, not a loss — and a duplicate is
a failure the inbox can absorb, where a silent loss is not.

Duplicate detection is time-bounded, though (five minutes on the emulator). If the republish
lands after that window has passed, the broker no longer recognises the id and the copy gets
through. Nothing breaks: the worker checks its own output store before analysing, and the
API's inbox catches the resulting event. The broker's check is the cheap first line, not the
guarantee.
See [ADR 0001](adr/0001-transactional-outbox-for-command-publishing.md).

**Edge-triggered**
Acting in response to an event ("a message arrived"). Efficient, but if the event is missed,
nothing ever happens and nothing notices. Most of this service is edge-triggered.
Contrast *level-triggered*.

**Idempotent**
Safe to do more than once: the second attempt changes nothing. Registering a document is
idempotent because it is keyed on a client-supplied id; applying an analysis result is
idempotent because of the inbox. Required here, because delivery is at-least-once.

**Inbox**
A table recording which messages have already been handled, keyed on `(message id, handler)`.
The row is written in the same transaction as the change it guards, so "I handled this" and
"here is the effect of handling it" commit together or not at all. `ProcessedMessages`.
See [ADR 0002](adr/0002-accept-the-inbox-recheck-race.md).

**Level-triggered**
Acting on the current state ("this document has been Analyzing for 20 minutes") rather than
on an event. Slower and more repetitive, but self-correcting: if one pass misses something,
the next pass sees the same state and acts anyway. The sweep is level-triggered.
See [ADR 0004](adr/0004-reconcile-stuck-analyses-with-a-level-triggered-sweep.md).

**Liveness probe**
`/health/live`. Answers "is this process still working?" A failure gets the container
restarted, so it checks nothing external — testing the database here would restart every
replica during a brief database outage.

**Optimistic concurrency**
Letting two writers proceed without locking, and detecting the collision when they save
instead. The `RowVersion` column changes on every update, so a writer working from a stale
copy fails and must re-read. Not enforced by the in-memory provider.

**Outbox**
A table where a message to be sent is written in the same transaction as the data change
that causes it. The message survives a crash because it is part of the same commit; a
separate process publishes it afterwards.

There is no status column on an outbox row, and nothing marks a row "pending". The row is
pending because `SentAtUtc` is null — an absence, not a value. It is null from the moment
the row is inserted, and the drainer sets it to the current time once the publish has
succeeded. One nullable timestamp carries both facts, whether it was sent and when, so there
is no second column that could disagree with the first.

That column is also the only thing the poller queries, so the schema indexes exactly it:
a filtered index over `(SentAtUtc, CreatedAtUtc)` with `WHERE [SentAtUtc] IS NULL`, named
`IX_OutboxMessages_Pending`. Because it excludes sent rows, the index stays near-empty once
a backlog drains, however large the table itself grows.

`Attempts` and `LastError` sit alongside. They are not a status either — they are the record
of what went wrong, which is what makes a permanently failing message visible instead of
silently looping.
See [ADR 0001](adr/0001-transactional-outbox-for-command-publishing.md).

**Poller**
A background service that wakes on a timer and checks for work. `OutboxPoller` checks the
outbox table every five seconds. Note that it polls *the database*, not the broker —
messages arriving from the broker are pushed down a connection the listener holds open.

**Readiness probe**
`/health/ready`. Answers "should this replica receive traffic?" A failure only removes it
from the load balancer. It checks the database, but deliberately not the broker, because the
outbox lets this service keep accepting work while the broker is unreachable.

**Reconciliation**
Comparing what the system's state says should have happened against what actually happened,
and repairing the difference. Here: documents still `Analyzing` long after they should have
finished.
See [ADR 0004](adr/0004-reconcile-stuck-analyses-with-a-level-triggered-sweep.md).

**Sweep**
A job that runs on a timer, goes through records looking for ones in a bad state, and fixes
them. Named after sweeping a floor — methodically over the whole area, rather than reacting
to one spot. Elsewhere the same idea is called a sweeper, a reaper or a janitor.
`StuckAnalysisScheduler` schedules it; `StuckAnalysisReconciler` does the work.

**Traceparent**
A standard header (W3C Trace Context) carrying a trace id from one service to the next, so
log lines and spans on both sides of a network hop can be pulled together. It rides inside
the message across the broker. The outbox stores it, so a publish that happens seconds later
still joins the trace of the request that queued it.

**Transaction**
A group of database changes that either all take effect or none of them do. If anything
fails partway through — an error, a lost connection, a process that dies — the database
undoes everything the group had done so far, so it is never left holding half of a change.
This all-or-nothing property is called *atomicity*; when two writes are described here as
"not atomic", this is what they lack.

It is what makes the outbox work: the document's status change and the message row are
written inside one transaction, so there is no instant where one exists without the other.

A transaction covers one database. It cannot stretch to a second system such as a message
broker, which is exactly what makes a *dual write* unsafe. Protocols exist that attempt it
across systems — two-phase commit — but they tie the availability of both systems together,
and this repo does not use them.

In Entity Framework Core, one `SaveChanges` call is one transaction: everything tracked
since the previous save is written as a single unit. That is why parts of this code take
care to make several changes in a single `SaveChanges` rather than in separate calls.

**Vertical slice**
Organising code by feature rather than by technical layer: one folder holds a feature's
endpoint, its handlers and its data shapes. Architecture tests enforce that slices do not
reference each other.
