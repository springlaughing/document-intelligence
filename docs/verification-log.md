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

### 5. Liveness and readiness answer different questions

**Tested:** that the two health endpoints fail independently, and that readiness ignores a
broker outage on purpose.

**How:** started the stack, then stopped and restarted each dependency in turn, polling
`/health/live` and `/health/ready`.

**Result:** passed.

| state | `/health/live` | `/health/ready` |
|---|---|---|
| Everything up | `200 Healthy` | `200 Healthy` |
| Database stopped | `200 Healthy` | `503 Unhealthy` |
| Broker stopped, database up | `200 Healthy` | `200 Healthy` |

A database outage takes the replica out of the load balancer without restarting it. A broker
outage changes nothing, because the outbox lets the API keep accepting work — the behaviour
recorded in entry 4.

The compose healthcheck reports `healthy` in both of the last two states, since it calls
`/health/ready`.

---

### 6. Multiple API replicas behind a load balancer

**Tested:** that the API runs as several replicas — traffic spread across them, one able to
die without taking requests with it, and the messaging layer unaffected by there being three
of everything.

**How:** `docker compose up --scale api=3`, with Traefik in front discovering replicas from
the Docker socket and polling `/health/ready`. Registered documents through the proxy, then
sent `SIGKILL` to one replica midway through a second run.

**Result:** passed, after fixing a defect the test found (below).

| | |
|---|---|
| 30 requests, all replicas up | 10 / 10 / 10 |
| 60 requests, one replica killed at 20 | 59 succeeded, 1 failed |
| Same run with Traefik's retry middleware | **60 succeeded, 0 failed** |
| Concurrency conflicts across replicas | 0 |
| Outbox publish failures across replicas | 0 |

The single failure without retry is the request in flight when the replica was killed:
health checks only notice afterwards. Retry resends it to another replica, which is safe
here because the endpoints are idempotent.

**Defect found and fixed: startup migrations could not survive more than one replica.** On
the first attempt two of the three replicas exited immediately — all three called
`MigrateAsync` at startup and raced to create the database, and two died on a command
timeout. EF Core does lock migrations, but the collision was in *database creation*, which
happens before a lock can be taken in a database that does not exist.

Migrating is now a separate run of the same image (`--migrate-only`), which applies
migrations and exits. In compose it is a one-shot `migrator` service the API waits on via
`service_completed_successfully`; replicas serving traffic no longer migrate at all. After
that change all three replicas started cleanly.

---

## Not covered

- **Worker killed while handling a message.** Entry 3 killed a worker between messages, not
  during one, so broker redelivery of a command was not exercised. Forcing this needs fault
  injection to make the handler slow enough to interrupt.
- **Broker partition.** Entry 4 stopped the broker cleanly. A broker that is reachable but
  timing out, or an outage longer than the one-hour message TTL, is untested.
- **Sustained load against several API replicas.** Entry 6 used tens of requests, enough to
  show distribution and survival but not enough to provoke the inbox race ADR 0002 describes.

Both services running together is no longer only checked by hand: `WorkerRoundTripTests`
starts the worker's real image alongside the broker and database, puts a command on the
queue, and asserts the document ends `Analyzed` carrying the worker's own summary and blob
reference.
