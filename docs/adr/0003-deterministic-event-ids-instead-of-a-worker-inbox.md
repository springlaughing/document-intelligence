# 0003 — Deterministic event ids instead of an inbox in the analysis service

**Status:** Accepted · 2026-07-31

## Context

`DocumentService.Api` guards against duplicate delivery with an inbox table keyed on the
event id, written in the same transaction as the change it guards ([0001](0001-transactional-outbox-for-command-publishing.md)
made that guard load-bearing, since outbox publishing is at-least-once).

`AnalysisService.Worker` had no equivalent, and its handler generated a fresh event id
per invocation:

```csharp
evt = new AnalysisCompletedEvent(Guid.NewGuid(), cmd.DocumentId, summary, blobRef);
```

The comment above that line claimed a redelivered command would reuse the id. It does not
— `Guid.NewGuid()` returns a new value every call. So a redelivered `AnalyzeDocumentCommand`
produced an event with a *different* id, the API's inbox saw something it had never
handled, and the duplicate was applied. The consumer-side guard was being defeated by the
producer.

The obvious remedy is an inbox in the worker too. That means giving it a database: a
`DbContext`, migrations, another container, and a schema it owns. The worker currently has
no store at all, which is what lets any instance take any message and lets it scale by
running more copies.

## Decision

Keep the worker stateless. Give `AnalyzeDocumentCommand` a `CommandId`, and derive the
event id from it rather than generating one:

```csharp
DeterministicId.From(cmd.CommandId, "analysis-completed")   // SHA-256, first 16 bytes
```

A redelivered command therefore produces a byte-identical event, which the API's existing
inbox recognises and discards. A genuine re-analysis arrives as a new request with a new
`CommandId`, and is applied as the new result it is.

**Prefer making duplicate work harmless over adding state to prevent it.**

## Consequences

**The work is still repeated; only the effect is deduplicated.** A redelivered command
re-runs the analysis and re-writes the blob. That is acceptable precisely because the
analysis here is a stub and the blob write is idempotent — the same document always maps
to the same path with the same content.

**This trade has a stated expiry.** If analysis becomes genuinely expensive — a real OCR
pass, a model call, anything billed per invocation — then repeating it stops being
acceptable and the worker needs its own inbox. The cheapest form would not be a relational
database but a check for the existence of the output blob, since object storage is already
in the path.

**The correctness now depends on two services agreeing.** The worker must derive ids
deterministically and the API must key its inbox on them. Neither can be changed alone,
which is a coupling the code does not express — hence this record, and the tests in
`AnalyzeDocumentCommandHandlerTests` that pin the derivation.

**Completed and failed events derive from the same command**, so the purpose string is
part of the hash input. Without it both would collapse to one id.

## Alternatives considered

**Give the worker a database and a real inbox.** The architecturally conventional answer,
and the right one once the work being guarded is expensive. Rejected for now as
disproportionate: a store whose only job is to avoid repeating a stub.

**Rely on Service Bus duplicate detection.** Already enabled on the `analyze-document`
queue, and it does close a real gap — the outbox relay republishing the same message. But
it dedupes *sends*, not *deliveries*: a message redelivered after a lock expiry or an
abandon is the same send, so the broker has no reason to suppress it. It is a complement,
not a substitute.

**Pass the broker's MessageId into the handler.** Would avoid changing the contract, but
requires threading transport concerns through `IMessageHandler<T>`, which exists precisely
so handlers do not know they are handling messages.
