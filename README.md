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

## Running Locally

```bash
dotnet build
dotnet run --project src/DocumentService.Api
dotnet run --project src/AnalysisService.Worker
```

![CI](https://github.com/springlaughing/document-intelligence/actions/workflows/ci.yml/badge.svg)
