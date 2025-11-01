# Document Intelligence

A modular .NET 8 demo application showcasing a modern, cloud-ready architecture with clean separation of concerns, automated CI, and optional local message-bus emulation.

# ✨ Overview

Document Intelligence simulates a document-processing platform consisting of:

- DocumentService.Api – exposes endpoints to upload and analyze documents

- AnalysisService.Worker – consumes analysis commands and publishes results

- Contracts – shared domain models, DTOs, and message contracts

- CI Pipeline – GitHub Actions workflow that builds and tests every push

- Microsoft Azure Service Bus Emulator – optional local replacement for cloud messaging

The system follows Clean Architecture + Vertical Slice principles to remain modular, testable, and ready for containerized deployment.

---

## Project Structure

```
src/
├─ DocumentService.Api/            # Web API (controllers, repositories, messaging)
├─ AnalysisService.Worker/         # Background worker (document analysis)
└─ DocumentIntelligence.Contracts/ # Shared Contracts (commands, DTOs, enums)

tests/
├─ DocumentService.Api.Tests/      # Unit / endpoint tests
└─ AnalysisService.Worker.Tests/   # Worker-specific tests
```

---

## 🧠 Technologies

- .NET 8 / C# 12

- ASP\.NET Core Web API

- Worker Service Template

- Entity Framework Core (InMemory) for local persistence

- Azure Service Bus SDK (Azure.Messaging.ServiceBus)

- Service Bus Emulator (for offline development)

- xUnit + Moq for automated testing

- GitHub Actions CI

- Docker / docker-compose for multi-container dev setup

---

# 🏗️ Architecture & Design

| Layer / Component | Description |
|-------------------|-------------|
| **Contracts** | Defines shared DTOs, domain enums, and messaging contracts |
| **API** | Exposes endpoints like `/api/documents/{id}/analyze`, persists data, and publishes commands |
| **Worker** | Listens to message queue (`analyze-document`) and performs background analysis |
| **Messaging** | In local mode uses emulator; in cloud mode targets real Azure Service Bus |
| **Security** | Local JWTs via `dotnet user-jwts`; cloud mode uses Entra ID (Azure AD) |
| **CI/CD** | GitHub Actions pipeline builds + tests on every push/PR (`Dev → Main`) |

---

## 🐳 Docker Quick Start (API + Worker + Service Bus Emulator)

> Runs everything in containers. No local SDKs required except a small step to mint a dev JWT + signing key.

### Prereqs

- Docker Desktop (or Docker Engine)

- .NET 8 SDK (only to generate a dev JWT & signing key)

### 1. Clone & prepare env files
```bash
git clone <your-repo-url>
cd <repo-root>

# create local env files from templates
cp docker/env/api.env.sample docker/env/api.env
cp docker/env/worker.env.sample docker/env/worker.env
cp docker/env/sqledge.env.sample docker/env/sqledge.env
cp docker/env/servicebus.env.sample docker/env/servicebus.env

```
> The real *.env files are gitignored. Only the *.sample files are committed.

### 2. Create a dev JWT and get its signing key (one-time)

Generate the token from inside the API project folder so the key belongs to this project.

```bash
cd src/DocumentService.Api

# (optional) reset dev signing key for a clean start
dotnet user-jwts reset

dotnet user-jwts create --project src/DocumentService.Api --audience api-dev --issuer http://localhost/ --scope read --scope write --role admin

# print signing key values (Id + Value)
dotnet user-secrets list

```
From the output, copy:

`Authentication:Schemes:Bearer:SigningKeys:0:Id` → KeyId

`Authentication:Schemes:Bearer:SigningKeys:0:Value` → KeyValue

### 3. Paste signing key into the API env

Edit docker/env/api.env and set these lines (replace placeholders):

```env
AUTH_MODE=userjwts
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://+:8080

# Service Bus emulator (inside compose network)
AzureServiceBus__ConnectionString=Endpoint=sb://servicebus/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=local;UseDevelopmentEmulator=true;
AzureServiceBus__AnalyzeDocumentQueueName=analyze-document
AzureServiceBus__AnalysisCompletedTopicName=analysis-completed
AzureServiceBus__AnalysisCompletedSubscriptionName=document-api

# JWT validation (must match the token you created)
Authentication__Schemes__Bearer__ValidIssuer=http://localhost/
Authentication__Schemes__Bearer__ValidAudiences__0=api-dev
Authentication__Schemes__Bearer__SigningKeys__0__Issuer=http://localhost/
Authentication__Schemes__Bearer__SigningKeys__0__KeyId=<KeyId from user-secrets>
Authentication__Schemes__Bearer__SigningKeys__0__Value=<KeyValue from user-secrets>

```
> You do not put the JWT token itself into env — only the signing key & validation settings.

### 4. Start everything

From the repo root:
```bash
docker compose up --build
```
Services that come up:

* `sqledge` – SQL Edge (persistence for the emulator)

* `servicebus` – Azure Service Bus Emulator

* `api` – DocumentService.Api

* `worker` – AnalysisService.Worker

### 5. Call the API

Open Swagger: http://localhost:5140/swagger

Click Authorize, choose Bearer, and paste the JWT token you created in step 2.
Now hit the endpoints.

![CI](https://github.com/springlaughing/document-intelligence/actions/workflows/ci.yml/badge.svg)
