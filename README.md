# Document Intelligence

A modular .NET 8 demo application designed to showcase modern **cloud-ready architecture**, including:

- **Clean Architecture + Vertical Slice Pattern**
- **Service separation:** `DocumentService.Api`, `AnalysisService.Worker`, and `Contracts`
- **Microservice-ready messaging model**
- **In-memory messaging (local) → Azure Service Bus (cloud)**
- **CI/CD and environment branching strategy** (Dev / Test / Prod)

---

## Project Structure

```
src/
├─ DocumentService.Api/ # Web API (Endpoints, Repositories, Messaging)
├─ AnalysisService.Worker/ # Background worker (Message consumer)
└─ DocumentIntelligence.Contracts/ # Shared Contracts (Commands, DTOs, Status)
```

---

## 🧠 Technologies

- .NET 8 (C#)
- ASP\.NET Core Minimal APIs
- Worker Service
- Entity Framework Core (InMemory)
- Docker (multi-container setup)
- Azure-ready configuration

---

## Architecture

- Clean Architecture + Vertical Slice Pattern
- Each endpoint is a feature slice (`UploadDocument`, `AnalyzeDocument`, etc.)
- Services communicate via Message Bus (InMemory → Service Bus)
- Internal authentication via shared secret / managed identity

---

## Running Locally

```bash
dotnet build
dotnet run --project src/DocumentService.Api
dotnet run --project src/AnalysisService.Worker
```