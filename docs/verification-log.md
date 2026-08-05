# Verification log

These tests cover what the unit and integration tests do not: the whole system running
together, with failures simulated inside it — worker instances killed, the broker stopped.

They are run manually. Each entry states what was tested, how, and the result.

**Environment for all entries below:** `docker compose up`, with SQL Edge, the Azure
Service Bus emulator, `DocumentService.Api`, and `AnalysisService.Worker`. Requests are sent
to the API at `http://localhost:5140` with a JWT.

---

## 2026-08-05

### 1. Work is distributed across worker instances

**Tested:** that several worker instances share the command queue without any routing
configuration.

**How:** started 3 workers (`docker compose up --scale worker=3`). Registered 6 documents and
called `POST /api/documents/{id}/analyze` for each.

**Result:** passed.

| | |
|---|---|
| Commands handled by worker-1 / 2 / 3 | 2 / 1 / 3 |
| Documents reaching `Analyzed` | 6 of 6 |

---

### 2. A duplicated command is analysed by every instance, but applied once

**Tested:** what happens when the same command reaches several worker instances.

**How:** with 3 workers running, published 3 messages directly to the `analyze-document`
queue. All 3 carried the same `CommandId`, each with a different broker `MessageId`.

**Result:** passed, with the expected duplicate work.

| | |
|---|---|
| Messages delivered | 3, one per worker |
| Skipped by the output store | 0 |
| Analyses executed | 3 |
| Blob paths written | 1 |
| Event ids published | 1 |
| Effects applied to the document | 1 |
| `ProcessedMessages` rows | 1 |

The document ended `Analyzed` with one summary and one blob reference.

The 3 analyses are expected: `InMemoryAnalysisResultStore` is a dictionary held in each
process, so no worker could see that a sibling had already handled the command. This is the
limitation described in
[ADR 0003](adr/0003-deterministic-event-ids-instead-of-a-worker-inbox.md) and it costs
duplicate work, not a duplicate result.

---

### 3. Killing a worker mid-run does not lose work

**Tested:** that losing a worker instance during a run costs nothing.

**How:** started 3 workers. Registered 100 documents and triggered analysis for all of them.
Sent `SIGKILL` to worker-1 (`docker kill`) after 25 requests.

**Result:** passed.

| | |
|---|---|
| worker-1 | exited, code 137 |
| Commands handled by worker-2 / 3 | 50 / 50 |
| Documents reaching `Analyzed` | 100 of 100 |
| `ProcessedMessages` rows | 100 (no duplicates applied) |
| Outbox rows still pending | 0 |

---

### 4. The API keeps working while the broker is down, and recovers on its own

**Tested:** that a broker outage delays work instead of losing it, and that recovery needs no
operator action.

**How:** started 2 workers. Registered 5 documents, then stopped the emulator
(`docker stop documentintelligence-servicebus-1`) and called the analyze endpoint for all 5.
Restarted the emulator afterwards (`docker start`) without touching the API.

**Result:** passed.

While the broker was stopped:

| | |
|---|---|
| Analyze endpoint responses | `202` × 5 |
| Documents | 5 `Analyzing` |
| Outbox rows pending | 5 |
| Outbox poller | retrying, still running |

After the broker was restarted:

| | |
|---|---|
| Outbox rows pending | 0 (drained automatically) |
| Documents reaching `Analyzed` | 5 of 5 |
| Commands handled by worker-1 / 2 | 2 / 3 |
| API restarts required | 0 |

---

## Not covered

- **Worker killed while handling a message.** Entry 3 killed a worker between messages, not
  during one, so broker redelivery of a command was not exercised. Forcing this needs fault
  injection to make the handler slow enough to interrupt.
- **Broker partition.** Entry 4 stopped the broker cleanly. A broker that is reachable but
  timing out, or an outage longer than the one-hour message TTL, is untested.
- **Multiple API replicas** competing on the `document-api` subscription.
- **Automated coverage of both services together.** `DocumentIntelligence.IntegrationTests`
  runs against a real broker and database but never starts `AnalysisService.Worker`, so no
  test in CI covers API → outbox → queue → worker → topic → API inbox.
