# Onboarding Guide

Goal: from clone to a running API and a mental model of the architecture, in under an hour. When something here conflicts with reality, fix one of the two the same day.

## 1. Prerequisites

- **.NET SDK 10** — the exact version pinned in `api/global.json` (`dotnet --version` must match its `rollForward` policy)
- **Docker** with Compose v2 (local PostgreSQL + observability stack)
- Any editor; Rider / VS Code with C# Dev Kit / Visual Studio all work — formatting and style are tool-enforced, so editor choice is free

## 2. Bootstrap and Run

```bash
./init                      # renders .env, restores pinned tools, installs git hooks (idempotent)
docker compose up -d        # PostgreSQL + Grafana observability stack
cd api
dotnet ef database update --project src/Infrastructure   # apply migrations locally
dotnet run --project src/Api                             # http://localhost:8080/api
```

Sanity checks:

- `GET http://localhost:8080/api/public/health` → `200`
- API reference UI (Development only): `http://localhost:8080/api/docs`
- Grafana (local traces/logs/metrics): `http://localhost:3333`

## 3. First Architecture Trace

Before writing code, walk one request through the layers **in dependency order**. Use the users listing:

| Step | File |
|---|---|
| 1. Endpoint (driving adapter) | `src/Api/Features/Users/Endpoints/GetUsers/GetUsersEndpoint.cs` |
| 2. Use case (domain) | `src/Domain/Features/Users/UseCases/GetUsers/GetUsersUseCase.cs` |
| 3. Port (domain contract) | `src/Domain/Features/Users/Ports/IUserRepositoryPort.cs` |
| 4. Adapter (driven, infrastructure) | `src/Infrastructure/Adapters/UserRepository.cs` |
| 5. Response mapping | `GetUsersEndpointResponse.cs` next to the endpoint |

Verify while tracing: business decisions live in the domain classes; the endpoint and the repository only **translate and perform I/O**. If you find a business branch in step 1, 4, or 5, you've found a bug — see [Architecture Overview §4](02-architecture-overview.md#4-what-belongs-in-the-domain-vs-in-adapters).

## 4. The Daily Loop

```bash
./validate        # format → build → arch tests → unit → integration → coverage → mutation
```

Run it **before every push**. It is the same gate set CI runs ([toolchain doc §5](06-build-toolchain-and-quality-gates.md#5-command-intent)); CI should confirm what you already know. When it fails, follow the [triage order](06-build-toolchain-and-quality-gates.md#51-failure-triage-order).

Two guardrails you'll meet in week one:

- **Query-count assertions:** database adapter tests declare their expected SQL statement count. If your change alters the count, that's a reviewed contract change — see [Testing Platform §8](05-testing-platform.md#8-query-count-guardrails).
- **Architecture tests fail first:** a misplaced or misnamed class fails the build before any behavior test runs. The error message names the violated rule; [doc 03](03-project-structure-and-conventions.md) tells you where the type belongs.

## 5. Common Pitfalls

- **Business logic drifting into adapters** because the endpoint or repository is "right there". Ownership, not proximity — the domain decides, adapters translate.
- **Tests without shared helpers**: if your test needs five lines of setup noise, add or extend a fixture helper in `TestHelpers` instead of copy-pasting — readable tests are a platform feature, not a luxury.
- **Treating rules as optional under pressure.** Every rule here is machine-enforced precisely so that deadline pressure cannot negotiate with it. The escape hatch is changing the rule (with rationale, in review) — never bypassing it.

## 6. Where to Go Next

- The architectural contract: [Architecture Overview](02-architecture-overview.md)
- Where everything lives: [Project Structure and Conventions](03-project-structure-and-conventions.md)
- Building the project from scratch, file by file: [Hands-On Build Guide](12-hands-on-build-guide.md)

## Navigation

⬅️ Previous: [Developer Guide](README.md) · ➡️ Next: [Architecture Overview](02-architecture-overview.md)
