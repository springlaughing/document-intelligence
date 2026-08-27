# Document Intelligence

![CI](https://github.com/springlaughing/document-intelligence/actions/workflows/ci.yml/badge.svg)

A .NET 8 worked example of an asynchronous, message-driven system: an API that accepts work
and a worker that performs it, joined by a broker rather than by a method call.

The interesting part is not the analysis — that is a stub. It is what the system does when
the pieces fail: when the broker is down, when a message arrives twice, when a worker dies
mid-message, when a command is lost entirely, and when the API runs as more than one
replica. Each of those has a pattern behind it, and the reasoning is recorded in
[docs/adr/](docs/adr/).

---

## Overview

Three runtime pieces and two shared libraries:

- **DocumentService.Api** — registers documents, accepts analysis requests, records results
- **AnalysisService.Worker** — consumes analysis commands and publishes outcomes
- **Azure Service Bus** — a real namespace in the cloud, the Microsoft emulator locally
- **DocumentIntelligence.Contracts** — the message types both sides agree on
- **DocumentIntelligence.Messaging** — the publish/dispatch plumbing both sides share

The API is organised as vertical slices: a feature owns its endpoint, its handlers and its
DTOs, and reaches persistence only through a repository interface. Architecture tests
enforce that slices do not reference each other and that only `Contracts` crosses the
service boundary.

---

## How a request flows

```
POST /api/documents/{id}/analyze
        │
        │  one transaction: status → Analyzing, AnalyzeDocumentCommand → Outbox table
        ▼
   OutboxPoller ──publish──▶ queue: analyze-document
                                  │
                                  ▼
                        AnalysisService.Worker
                         analyses, stores result
                                  │
                    ┌─────────────┴─────────────┐
                    ▼                           ▼
        topic: analysis-completed     topic: analysis-failed
                    │                           │
                    └────────► subscription: document-api
                                       │
                                       │  inbox: (MessageId, Handler) seen before?
                                       ▼
                          document → Analyzed / Failed
```

The endpoint answers `202 Accepted` as soon as the transaction commits. Nothing in the
request path talks to the broker, which is why the API keeps accepting work while Service
Bus is unreachable.

---

## Reliability patterns, and why each is here

| Pattern | Problem it solves | Where |
|---|---|---|
| **Transactional outbox** | A status change and a published command cannot be made atomic across two systems. Writing the message to the same database in the same transaction can be, and a poller publishes it afterwards. | [ADR 0001](docs/adr/0001-transactional-outbox-for-command-publishing.md) |
| **Inbox** | Service Bus delivers at least once. Results are recorded against `(MessageId, Handler)` so a redelivered event is applied exactly once. | [ADR 0002](docs/adr/0002-accept-the-inbox-recheck-race.md) |
| **Deterministic event ids** | The worker has no database, so it cannot keep an inbox. Its event ids are derived from the command, and its output store answers "have I already done this?". | [ADR 0003](docs/adr/0003-deterministic-event-ids-instead-of-a-worker-inbox.md) |
| **Reconciliation sweep** | Neither outbox nor inbox can see a command that dead-lettered — nothing arrives to be handled. A timer reads state instead, re-queues documents stuck in `Analyzing`, and fails them after a bounded number of attempts. | [ADR 0004](docs/adr/0004-reconcile-stuck-analyses-with-a-level-triggered-sweep.md) |
| **Dead-lettering** | A message that can never succeed must leave the queue instead of poisoning it. `UnprocessableMessageException` dead-letters immediately rather than burning ten deliveries. | `DocumentIntelligence.Messaging` |
| **Retention cleanup** | Outbox and inbox are append-only tables. Without a sweep they grow forever. | `Infrastructure/Outbox` |
| **Distributed tracing** | The wire hop is the one place Go-to-Definition cannot follow. W3C `traceparent` rides inside the message, and the outbox restores the trace of the request that queued it. | both `Program.cs` files |
| **Split migrations** | Three replicas calling `MigrateAsync` at startup raced to create the database and two died. Migrating is now `--migrate-only`, a separate run of the same image. | `docker-compose.yml`, `migrator` |
| **Liveness vs readiness** | A liveness failure restarts the container, so `/health/live` checks nothing. `/health/ready` checks the database — but deliberately **not** the broker, because the outbox exists so the API can serve without it. | `Program.cs`, health checks |

---

## Project structure

```
src/
├─ DocumentService.Api/             # Web API — vertical slices, EF Core, outbox, inbox
│  ├─ Features/Documents/           #   RegisterDocument, GetDocument,
│  │                                #   RequestAnalysis, RecordAnalysisResult
│  ├─ Domain/
│  └─ Infrastructure/               #   Ef (+ migrations), Outbox, Repositories
├─ AnalysisService.Worker/          # Background worker — consumes commands, publishes events
├─ DocumentIntelligence.Contracts/  # Commands, events, DTOs shared across the boundary
└─ DocumentIntelligence.Messaging/  # Publisher, dispatcher, handler abstraction

tests/
├─ DocumentService.Api.Tests/       # Slice, repository, outbox and reconciler unit tests
├─ AnalysisService.Worker.Tests/    # Handler unit tests
├─ DocumentIntelligence.ArchitectureTests/  # Slice isolation and module boundaries
└─ DocumentIntelligence.IntegrationTests/   # Real SQL Server + Service Bus emulator
                                            # in Testcontainers; the worker runs from
                                            # its own image

docs/
├─ adr/                             # Architecture decision records
├─ glossary.md                      # What the words mean
└─ verification-log.md              # Manual failure-mode tests: what, how, result

docker/env/                         # Env templates (*.sample committed, real files ignored)
infrastructure/                     # Service Bus emulator topology
```

---

## Technologies

.NET 8 / C# 12 · ASP\.NET Core Web API · Worker Service · EF Core (SQL Server, with an
InMemory fallback) · Azure.Messaging.ServiceBus · Service Bus Emulator · OpenTelemetry
(+ Azure Monitor) · xUnit, Moq, Testcontainers · Traefik · Docker Compose · GitHub Actions

---

## 🐳 Running it (API + Worker + emulator + SQL)

Everything runs in containers. The only local SDK step is generating a dev JWT.

### Prereqs

- Docker Desktop (or Docker Engine)
- .NET 8 SDK — only to create a dev JWT and read its signing key

### 1. Clone and prepare env files

```bash
git clone <repo-url>
cd <repo-root>

cp docker/env/api.env.sample        docker/env/api.env
cp docker/env/worker.env.sample     docker/env/worker.env
cp docker/env/sqledge.env.sample    docker/env/sqledge.env
cp docker/env/servicebus.env.sample docker/env/servicebus.env
```

> The real `*.env` files are gitignored; only the `*.sample` files are committed.

### 2. Create a dev JWT and read its signing key (one-time)

```bash
# (optional) reset the dev signing key for a clean start
dotnet user-jwts reset --project src/DocumentService.Api

dotnet user-jwts create --project src/DocumentService.Api \
  --audience api-dev --issuer http://localhost/ \
  --scope read --scope write --role admin

# print the signing key (Id + Value)
dotnet user-secrets list --project src/DocumentService.Api
```

From the output, copy `…SigningKeys:0:Id` → **KeyId** and `…SigningKeys:0:Value` →
**KeyValue**.

### 3. Paste the signing key into the API env

In `docker/env/api.env`, replace the two placeholders:

```env
Authentication__Schemes__Bearer__SigningKeys__0__KeyId=<KeyId from user-secrets>
Authentication__Schemes__Bearer__SigningKeys__0__Value=<KeyValue from user-secrets>
```

> The JWT itself never goes into env — only the signing key and the validation settings.
> The sample also carries `ConnectionStrings__DocumentDb`; delete that line to fall back to
> the in-memory provider.

### 4. Start everything

```bash
docker compose up --build
```

What comes up, and in what order:

| Service | Role |
|---|---|
| `sqledge` | SQL Edge — the document database and the emulator's own storage |
| `servicebus` | Azure Service Bus emulator, topology from `infrastructure/servicebus-config.json` |
| `migrator` | One-shot: the API image run with `--migrate-only`, applies migrations, exits |
| `api` | DocumentService.Api — no host port, waits for `migrator` to succeed |
| `worker` | AnalysisService.Worker |
| `reverse-proxy` | Traefik — the only thing published, routes to healthy API replicas |

Run several API replicas:

```bash
docker compose up --build --scale api=3
```

Traefik discovers them from the Docker socket and polls `/health/ready`, so a replica that
cannot reach its database leaves the rotation instead of receiving requests it cannot serve.
This is why the API publishes no host port — a published port would cap it at one replica.

### 5. Call the API

| URL | |
|---|---|
| http://localhost:5140/swagger | Swagger UI — click **Authorize**, paste the JWT from step 2 |
| http://localhost:8081 | Traefik dashboard (which replicas are healthy) |
| http://localhost:5140/health/ready | Readiness (anonymous) |
| http://localhost:5140/health/live | Liveness (anonymous) |

Endpoints:

| | | |
|---|---|---|
| `POST` | `/api/documents` | Register a document — body `{ documentId, fileName }`, idempotent on `documentId` |
| `GET` | `/api/documents/{id}` | Read status, summary and blob reference |
| `POST` | `/api/documents/{id}/analyze` | Queue analysis — returns `202 Accepted`; poll the GET to watch it reach `Analyzed` |

In `Development` the API seeds three demo documents (`aaaaaaaa-…`, `bbbbbbbb-…`,
`cccccccc-…`), so you can call analyze without registering anything first.

---

## Running without Docker

```bash
dotnet run --project src/DocumentService.Api
```

With no `ConnectionStrings:DocumentDb` configured, the API uses the EF Core InMemory
provider and needs no containers. Be aware of what that costs: InMemory ignores the
`RowVersion` concurrency token, max lengths and unique constraints, so optimistic
concurrency, idempotency-by-unique-key and the outbox are not really being exercised.

Against a relational database, apply migrations first — the app no longer migrates on
startup, and logs a warning if migrations are pending:

```bash
dotnet run --project src/DocumentService.Api -- --migrate-only
```

The broker is still required for anything to be consumed; run at least the `sqledge` and
`servicebus` compose services.

---

## Tests

```bash
dotnet test
```

Four assemblies, all run in CI on every push and PR to `dev` and `main`:

- **Unit tests** — slices, repository, outbox drainer, cleaner, reconciler, worker handler.
- **Architecture tests** — slices do not reference each other; only `Contracts` crosses the
  service boundary. These fail the build when the structure drifts, which is the only way a
  boundary survives contact with deadlines.
- **Integration tests** — real SQL Server and the real Service Bus emulator in
  Testcontainers. `WorkerRoundTripTests` builds the worker's own image and runs it on the
  shared network, so the actual composition root is exercised: put a command on the queue,
  assert the document ends `Analyzed` carrying the worker's summary and blob reference.

CI also collects coverage and uploads an HTML report as an artifact.

---

## Documentation

- **[docs/glossary.md](docs/glossary.md)** — the vocabulary this repo uses in code, comments and decision
  records: outbox, inbox, sweep, dead-letter, level- and edge-triggered. Start here if a term
  in an ADR is unfamiliar.
- **[docs/adr/](docs/adr/)** — why the outbox, the inbox race, deterministic ids, and the
  reconciliation sweep are the way they are. Records are immutable; a changed decision gets
  a new record.
- **[docs/verification-log.md](docs/verification-log.md)** — failure modes tested by hand
  against the running system: work distributed across workers, a duplicated command applied
  once, a worker killed mid-run, the broker stopped and recovered, the two probes behaving
  differently, and several replicas behind the load balancer. Each entry states what was
  tested, how, and the result — including its **Not covered** section.

### Known gaps

Listed at the end of the verification log, in the order worth doing:

1. **Broker partition** — the broker was stopped cleanly, never left reachable-but-hanging.
   With retries backing off to 8s, each publish takes ~30s to fail; whether the outbox grows
   unboundedly while the API keeps returning `202` is untested.
2. **Worker killed mid-message** — redelivery is proven by test, but never triggered by an
   actual crash inside the handler.
3. **Sustained load** — enough concurrency to provoke the inbox race ADR 0002 accepts.
