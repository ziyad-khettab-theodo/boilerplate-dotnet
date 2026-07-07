# Project Structure and Conventions

This document is the source of truth for where code lives and what it is called. The taxonomy is not cosmetic: architecture tests assert placement and naming, so a misplaced or misnamed type fails the build.

## 1. Repository and Solution Layout

```text
/                                  # repository root: docs/, docker compose files, CI, init
└── api/                           # the REST API component
    ├── DotnetBoilerplate.slnx     # solution file
    ├── global.json                # SDK version pin + test runner selection
    ├── Directory.Build.props      # build policies shared by every project
    ├── Directory.Packages.props   # central package version management
    ├── .config/dotnet-tools.json  # pinned local CLI tools
    ├── src/
    │   ├── Domain/                # Theodo.DotnetBoilerplate.Domain
    │   ├── Infrastructure/        # Theodo.DotnetBoilerplate.Infrastructure
    │   └── Api/                   # Theodo.DotnetBoilerplate.Api (host)
    └── tests/
        ├── ArchitectureTests/     # rule suite — runs first
        ├── Domain.UnitTests/      # references Domain only
        ├── Api.IntegrationTests/  # boundary + application tests
        └── TestHelpers/           # shared fakes, fixtures, base classes
```

Projects are named with the full root namespace (`Theodo.DotnetBoilerplate.<Project>`); folders use the short segment. Namespaces always match the folder path (`Theodo.DotnetBoilerplate.Domain.Features.Users.UseCases.Signup`), one type per file, file named after the type.

**Why:** predictable placement means any engineer (or tool) can navigate by convention instead of memory; namespace = folder keeps moves honest.

**Enforced by:** `tests/ArchitectureTests/NamingConventionRulesUnitTests.cs`; IDE analyzers (namespace-folder mismatch is a build warning, and warnings are errors).

## 2. Domain Project Structure

One folder per feature under `Features/`, plus the shared kernel:

```text
src/Domain/
├── Features/
│   └── <Feature>/                 # e.g. Users, Authentication, Audit
│       ├── Entities/              # domain objects with identity (records)
│       ├── ValueObjects/          # immutable values with invariants (records/enums)
│       ├── Ports/                 # I*Port interfaces the feature needs
│       ├── UseCases/
│       │   └── <UseCase>/         # e.g. Signup/
│       │       ├── <Name>UseCase.cs
│       │       └── <Name>Command.cs   # or <Name>Query.cs
│       ├── Events/                # <Name>Event records (facts that happened)
│       ├── Exceptions/            # feature domain exceptions
│       ├── Services/              # domain services (shared business behavior)
│       ├── Properties/            # domain-owned configuration records
│       └── IntegrationEvents/     # cross-feature contracts (records)
├── Common/
│   ├── Events/                    # IEvent, IIntegrationEvent marker interfaces
│   ├── Exceptions/                # DomainException base type
│   ├── Pagination/                # PageNumber, PageSize, PageQuery<T>, PageResult<T>…
│   ├── Ports/                     # shared ports: IEventPublisherPort, ITimeProviderPort,
│   │                              #   IRandomGeneratorPort, IPasswordEncoderPort
│   └── ValueObjects/              # shared value objects (e.g. Username, Role)
└── Utils/                         # generic, domain-agnostic helpers
```

### 2.1 Use Cases

- One class = one business operation; exactly one public `Handle` method with at most one input parameter (plus optional `CancellationToken`).
- Use cases depend on ports and domain services — **never on other use cases**.

**Why:** prevents multi-purpose classes and hidden orchestration coupling; every operation has one obvious home.
**Enforced by:** `tests/ArchitectureTests/Domain/UseCaseRulesUnitTests.cs`.

### 2.2 Commands and Queries

- Each use case owns its input record: `SignupCommand` (state change) or `GetUsersQuery` (read).
- Carries only the input for that one use case; never shared across use cases.

**Why:** sharing inputs couples unrelated operations — a field added for one silently reaches the other.
**Enforced by:** `tests/ArchitectureTests/Domain/NamingConventionRulesUnitTests.cs` (types under `UseCases` match `*UseCase|*Command|*Query`).

### 2.3 Domain Services

- Business behavior shared by several use cases **within the same feature** (e.g. token-pair creation used by both login and refresh).
- Not a dumping ground: technical helpers go to `Utils/` or adapters; single-use-case logic stays in the use case.

### 2.4 Entities vs. Value Objects

> **Concept primer.** An **entity** has an identity that persists over time (`User` with an `Id` — two users with the same name are still different users). A **value object** is defined only by its value (`Username`, `PageSize`) — two instances with equal content are interchangeable. Records give both structural equality; entities additionally carry an identity property.

- Value objects validate their invariants at construction and are impossible to create in an invalid state.
- Prefer value objects over raw `string`/`int` in domain signatures.

**Why:** "stringly-typed" code scatters validation everywhere; a `Username` type centralizes it and makes illegal states unrepresentable.

### 2.5 Domain Exceptions

- All domain exceptions extend `DomainException` (`Common/Exceptions`), are suffixed `Exception`, live in `Exceptions/`.
- Prefer explicit types with structured context (`UsernameAlreadyExistsException(Username username)`) over message-only exceptions.

**Why:** typed exceptions map deterministically to API error codes (see [Security, Observability and Error Handling](09-security-observability-and-error-handling.md#7-error-contract)); structured context makes logs and handlers reliable.
**Enforced by:** `tests/ArchitectureTests/NamingConventionRulesUnitTests.cs` (inheritance ⇔ naming ⇔ placement).

### 2.6 Domain Events

- Named for what **happened**, past tense: `UserSignedUpEvent`, `UserLoginFailedEvent`; implement `IEvent`.
- Carry only relevant data; include the source type for attribution; readable `ToString()` for logs.
- Published by use cases through `IEventPublisherPort` — never dispatched directly.

### 2.7 New Use Case Checklist

1. `Features/<Feature>/UseCases/<Name>/<Name>Command.cs` (or `Query`)
2. `Features/<Feature>/UseCases/<Name>/<Name>UseCase.cs`
3. New port needed? `Features/<Feature>/Ports/I<Name>Port.cs` + adapter in Infrastructure + fake in `TestHelpers`
4. Unit tests in `Domain.UnitTests` mirroring the folder path
5. Domain event if other features must react

## 3. Api Project Structure

```text
src/Api/
├── Program.cs                     # composition root
├── Features/
│   └── <Feature>/
│       ├── Endpoints/
│       │   └── <Endpoint>/        # e.g. Signup/
│       │       ├── <Name>Endpoint.cs
│       │       ├── <Name>EndpointRequest.cs
│       │       └── <Name>EndpointResponse.cs
│       ├── ExceptionHandlers/     # feature exception → status + error code mapping
│       ├── Schedules/<Name>/      # scheduled tasks (hosted services)
│       ├── Services/              # API-side shared logic (e.g. cookie composition)
│       ├── Mappers/               # transport ↔ domain enum/value mapping
│       ├── Listeners/             # integration-event consumers
│       └── Publishers/            # domain-event → integration-event publishers
└── Common/
    ├── Endpoints/                 # IEndpoint convention + discovery
    ├── ExceptionHandling/         # handler chain, ErrorCode contract, ProblemDetails
    ├── Security/                  # authentication setup, fallback policy, policies
    ├── ServiceRegistration/       # use-case convention registration, adapter bindings
    ├── Responses/                 # shared response bases (e.g. paged response)
    ├── Validation/                # custom validation attributes
    └── Properties/                # API-side options records
```

### 3.1 Endpoints

- One class per route, implementing the `IEndpoint` convention; request/response records local to the endpoint folder (full contract in [Architecture Overview §6](02-architecture-overview.md#6-endpoint-contract-repr)).
- Request fields carry validation attributes for transport shape; business invariants remain in the domain.
- Responses are explicit API contracts: map domain results field by field — never return domain or database types directly.

### 3.2 Scheduled Tasks

- Time-triggered work lives under `Features/<Feature>/Schedules/<Name>/` as `<Name>ScheduledTask`, delegating to a use case exactly like an endpoint does.
- Group only methods sharing the same dependency set; otherwise split.

**Why:** scheduled tasks are driving adapters — same translation-only rule as endpoints; grouping by dependencies avoids bloated scheduler classes.

### 3.3 API Services

- Shared API-side logic that several endpoints need (e.g. composing authentication cookies onto a response).
- Never business logic — if a decision depends on business rules, it belongs in the domain.

### 3.4 New Endpoint Checklist

1. `Features/<Feature>/Endpoints/<Name>/<Name>EndpointRequest.cs` — record, validation attributes on every field
2. `<Name>EndpointResponse.cs` — record with a `From(<domain type>)` factory
3. `<Name>Endpoint.cs` — route, explicit authorization, one use-case call
4. Web integration test in `Api.IntegrationTests` (status, payload, authorization)
5. New exception surfaced? Extend the feature exception handler + its handler tests

## 4. Infrastructure Project Structure

```text
src/Infrastructure/
├── Adapters/                      # port implementations (UserRepository, TimeProvider,
│                                  #   RandomGenerator, EventPublisher, JwtTokenClaimsCodec…)
├── Database/
│   ├── AppDbContext.cs
│   ├── Entities/                  # <Name>DbEntity — mutable persistence classes
│   ├── EntityConfigurations/      # IEntityTypeConfiguration<T> mappings
│   └── Logging/                   # query logging, SQL commenting
├── Configurations/                # technical wiring (interceptors, listeners)
├── Logging/                       # audit sink, structured log formatting
├── Mappers/                       # value-object ↔ primitive mapping helpers
├── Properties/                    # infrastructure options records (e.g. JwtProperties)
└── Tracing/                       # trace-id helpers
```

- Adapters map database entities ↔ domain models **both ways** and translate infrastructure exceptions into domain exceptions. Details in [Data Persistence and Migrations](08-data-persistence-and-migrations.md).
- Infrastructure is **centralized**: features do not get their own infrastructure folders. A feature's ports are implemented here, next to the other adapters.

**Why centralized:** driven adapters share heavy machinery (DbContext, mapping helpers, logging); centralizing avoids N copies of that machinery and keeps the domain the only per-feature axis. The boundary that matters — domain sees ports only — is unaffected by where adapters sit.

## 5. Common Modules — Responsibilities

| Module | Owns | Must not contain |
|---|---|---|
| `Api/Common` | host composition, security, exception handling, serialization, endpoint conventions | business logic |
| `Domain/Common` | shared domain abstractions (base exception, events, pagination, shared ports/VOs) | feature business entities |
| `Domain/Utils` | generic, dependency-light helpers | domain helper logic (that's `Domain/Common`) |
| `Infrastructure` | all driven adapters + technical config | business decisions; API concerns |

## 6. Naming Conventions (summary table)

| Concept | Pattern | Kind | Location |
|---|---|---|---|
| Use case | `<Name>UseCase`, one `Handle` | class | `Domain/Features/<F>/UseCases/<Name>/` |
| Use case input | `<Name>Command` / `<Name>Query` | record | same folder as its use case |
| Port | `I<Name>Port` | interface | `Domain/…/Ports/` |
| Adapter | descriptive name, implements a port | class | `Infrastructure/Adapters/` |
| Endpoint | `<Name>Endpoint` : `IEndpoint` | class | `Api/Features/<F>/Endpoints/<Name>/` |
| Transport models | `<Name>EndpointRequest` / `<Name>EndpointResponse` | record | endpoint's folder |
| Database entity | `<Name>DbEntity` | mutable class | `Infrastructure/Database/Entities/` |
| Domain entity | noun (`User`) | record | `Domain/…/Entities/` |
| Value object | noun (`Username`) | record/enum | `Domain/…/ValueObjects/` |
| Domain event | `<Fact>Event` : `IEvent`, past tense | record | `Domain/…/Events/` |
| Integration event | `<Fact>IntegrationEvent` : `IIntegrationEvent` | record | `Domain/…/IntegrationEvents/` |
| Exception | `<Name>Exception` : `DomainException` | class | `Domain/…/Exceptions/` |
| Error codes | enum implementing `ErrorCode` | enum | `Api/…/ExceptionHandlers/` |
| Scheduled task | `<Name>ScheduledTask` | class | `Api/Features/<F>/Schedules/<Name>/` |
| Mapper | `<Name>Mapper` | class | `Api|Infrastructure/…/Mappers/` |
| Test classes | `*UnitTests`, `*IntegrationTests`, `*ApplicationTests`, `*ContractTests`, `*RulesUnitTests` | class | see [Testing Platform](05-testing-platform.md) |

**Enforced by:** the `NamingConventionRulesUnitTests` classes per area, `UseCaseRulesUnitTests`, `EndpointConventionRulesUnitTests`, `DbEntityRulesUnitTests`.

## 7. Do / Do Not

✅ Do: create the full folder skeleton for a new feature even if some folders start empty — placement questions answered once.
✅ Do: keep one type per file, named identically.
❌ Do not: share transport models "for DRY" — duplication at boundaries is cheaper than coupling.
❌ Do not: expose `*DbEntity` types outside `Infrastructure`.
❌ Do not: put business branches in API services, mappers, or adapters.

## Navigation

⬅️ Previous: [Architecture Overview](02-architecture-overview.md) · ➡️ Next: [Project Bootstrap Checklist](04-project-bootstrap-checklist.md)
