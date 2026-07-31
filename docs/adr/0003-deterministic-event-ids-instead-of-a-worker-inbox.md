# 0003 — Deduplicate the analysis service through its output store, not a database

**Status:** Accepted · 2026-07-31

## Context

`DocumentService.Api` guards against duplicate delivery with an inbox table keyed on the
event id, written in the same transaction as the change it guards ([0001](0001-transactional-outbox-for-command-publishing.md)
made that guard load-bearing, since outbox publishing is at-least-once).

`AnalysisService.Worker` had no equivalent, and generated a fresh event id per
invocation:

```csharp
evt = new AnalysisCompletedEvent(Guid.NewGuid(), cmd.DocumentId, summary, blobRef);
```

The comment above that line claimed a redelivered command would reuse the id. It does
not — `Guid.NewGuid()` returns a new value every call. So a redelivered command produced
an event with a *different* id, the API's inbox saw something it had never handled, and
applied the duplicate. **The producer was defeating the consumer's guard.**

This service exists separately from the API specifically so that document analysis can
scale on its own. That is the whole reason for the split, and it means repeating the work
is expensive by design — a real OCR pass or model call, not the stub currently standing in
for one. Any reasoning that leans on "the analysis is cheap" is reasoning about the
placeholder rather than the architecture.

## Decision

Two guards, at two levels.

**Skip the work** by asking the output store whether this command has already been
analysed. `IAnalysisResultStore` is keyed by `CommandId`, so a redelivered command asks
for a key that exists while a genuine re-analysis brings a new one.

**Skip the effect** by deriving the event id from the command rather than generating it,
so a redelivered command republishes something the API's inbox recognises and discards.

Both are needed. Skipping the work does not mean the result was ever successfully
announced, so a skipped run still publishes — and that publication has to be
deduplicable downstream.

## Why the output store rather than an inbox

The deciding question is not how expensive the work is. It is **whether the effect is
observable**.

| Effect visible to this service? | Cost of repeating | Mechanism |
|---|---|---|
| Yes — output in a keyed store | any, including very high | check the output |
| No — external side effect | low | just repeat it |
| No — external side effect | high | an inbox of its own |

Analysis writes its result to object storage. That makes the effect directly observable:
"have I done this?" is a lookup on a key. Transcoding and image pipelines work exactly
this way, and they are not cheap — cost stops mattering once you can see the effect,
because checking is cheap regardless.

An inbox would mean giving this service a database, and that has a consequence beyond the
container: its handler would then write to that database *and* publish to the broker,
which is a dual write. **The inbox would create the need for an outbox.** Both are avoided
by the same fact — no database — which is not a coincidence but one decision viewed twice.

Statelessness is also the property that makes the service scalable in the first place. Any
instance can take any message; capacity is a matter of running more copies. Introducing
shared relational state to deduplicate would erode the reason the service was separated.

## Consequences

**Correctness now depends on two services agreeing.** The worker must derive ids
deterministically and the API must key its inbox on them. Neither can change alone, and
the code does not express that coupling — hence this record, and the tests in
`AnalyzeDocumentCommandHandlerTests` that pin the derivation.

**Completed and failed events derive from the same command**, so the purpose string is
part of the hash input. Without it the two would collapse into one id.

**The current store is a placeholder.** `InMemoryAnalysisResultStore` is a dictionary, so
deduplication holds only within one process for as long as it runs. Restart it, or run a
second instance, and a redelivered command is analysed again. That is a limitation of the
stand-in, not of the approach: a blob-backed implementation is durable and shared, which
is precisely why the design points at object storage.

**Results accumulate per command rather than per document.** Re-analysis writes a new
object instead of overwriting, which keeps history but needs a retention policy
eventually.

## Alternatives considered

**A database and a real inbox.** The conventional answer, and the right one for a service
whose side effects it cannot see. Rejected here because the effect *is* visible, and
because it would drag an outbox in behind it and cost the statelessness that justifies the
separate deployment.

**Service Bus duplicate detection.** Already enabled on the `analyze-document` queue and it
closes a real gap — the outbox relay republishing the same message. But it dedupes *sends*,
not *deliveries*: a message redelivered after a lock expiry is the same send, so the broker
has no reason to suppress it. A complement, not a substitute.

**Pass the broker's MessageId into the handler.** Avoids changing the contract, but
requires threading transport concerns through `IMessageHandler<T>`, which exists precisely
so handlers need not know they are handling messages.

## Known gap

If a command exhausts `MaxDeliveryCount` and dead-letters, no event is ever published and
the document remains `Analyzing` with nothing detecting it. Neither guard here addresses
that. A reconciliation sweep over documents stuck in `Analyzing` would — and would also
catch failure modes not enumerated anywhere, which is its real value.
