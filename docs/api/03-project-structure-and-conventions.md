# Project Structure and Conventions

This document is the source of truth for where code lives and what it is called. The taxonomy is not cosmetic: architecture tests assert placement and naming, so a misplaced or misnamed type fails the build.

The organizing principle is **feature-first**: you navigate by business capability, and inside each feature you find its hexagonal layers. Shared, cross-feature code lives under `Common`.

## 1. Repository and Solution Layout

```text
/                                  # repository root: docs/, docker compose files, CI, init
└── api/                           # the REST API component
    ├── Theodo.DotnetBoilerplate.slnx
    ├── global.json                # SDK version pin + test runner selection
    ├── Directory.Build.props      # build policies shared by every project
    ├── Directory.Packages.props   # central package version management
    ├── .config/dotnet-tools.json  # pinned local CLI tools
    ├── src/                       # ONE application project: Theodo.DotnetBoilerplate
    │   ├── Features/              # one folder per feature (subdomain)
    │   ├── Common/                # shared kernel: Api, Domain, Infra, Utils
    │   └── Program.cs             # composition root
    └── tests/
        ├── ArchitectureTests/     # rule suite — runs first
        ├── UnitTests/             # domain unit tests
        ├── IntegrationTests/      # boundary + application tests
        └── TestHelpers/           # shared fakes, fixtures, base classes
```

The application is a **single project** (one assembly): `Features/` and `Common/` are folders, not separate projects. The hexagonal layers are enforced by architecture tests (see [Architecture Overview §3](02-architecture-overview.md#3-dependency-direction-rules)), not by assembly boundaries.

Namespaces always match the folder path (`Theodo.DotnetBoilerplate.Features.Users.Domain.UseCases.Signup`), one type per file, file named after the type.

**Why one project:** the architecture is defined by the dependency *rules*, not by how many assemblies enforce them. A single project keeps a feature's slice readable in one place and adds no per-feature build ceremony; the rules are enforced uniformly by the architecture-test suite.

**Enforced by:** `tests/ArchitectureTests/NamingConventionRulesUnitTests.cs`; IDE analyzers (namespace-folder mismatch is a build warning, and warnings are errors).

## 2. Feature Domain Structure

A feature is a folder under `Features/`; its business core lives in `Domain/`:

```text
src/Features/<subdomain>/            # e.g. Users, Authentication, Audit
├── Domain/
│   ├── Entities/                  # domain objects with identity (records)
│   ├── ValueObjects/              # immutable values with invariants (records/enums)
│   ├── Ports/                     # I*Port interfaces this feature needs
│   ├── UseCases/
│   │   └── <UseCase>/             # e.g. Signup/
│   │       ├── <Name>UseCase.cs
│   │       └── <Name>Command.cs   # or <Name>Query.cs
│   ├── Events/                    # <Name>Event records (facts that happened)
│   ├── Exceptions/                # feature domain exceptions
│   ├── Services/                  # domain services (shared business behavior)
│   └── Properties/                # domain-owned configuration records
├── Api/                           # see §3
└── IntegrationEvents/             # cross-feature contracts (records) + publishers
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
- Not a dumping ground: technical helpers go to `Common/Utils` or adapters; single-use-case logic stays in the use case.

### 2.4 Entities vs. Value Objects

> **Concept primer.** An **entity** has an identity that persists over time (`User` with an `Id` — two users with the same name are still different users). A **value object** is defined only by its value (`Username`, `PageSize`) — two instances with equal content are interchangeable. Records give both structural equality; entities additionally carry an identity property.

- Value objects validate their invariants at construction and are impossible to create in an invalid state.
- Prefer value objects over raw `string`/`int` in domain signatures.

**Why:** "stringly-typed" code scatters validation everywhere; a `Username` type centralizes it and makes illegal states unrepresentable.

### 2.5 Domain Exceptions

- All domain exceptions extend `DomainException` (`Common/Domain/Exceptions`), are suffixed `Exception`, live in the feature's `Domain/Exceptions/`.
- Prefer explicit types with structured context (`UsernameAlreadyExistsException(Username username)`) over message-only exceptions.

**Why:** typed exceptions map deterministically to API error codes (see [Security, Observability and Error Handling](09-security-observability-and-error-handling.md#7-error-contract)); structured context makes logs and handlers reliable.
**Enforced by:** `tests/ArchitectureTests/NamingConventionRulesUnitTests.cs` (inheritance ⇔ naming ⇔ placement).

### 2.6 Domain Events

- Named for what **happened**, past tense: `UserSignedUpEvent`, `UserLoginFailedEvent`; implement `IEvent`.
- Carry only relevant data; include the source type for attribution; readable `ToString()` for logs.
- Published by use cases through `IEventPublisherPort` — never dispatched directly.

### 2.7 New Use Case Checklist

1. `Features/<subdomain>/Domain/UseCases/<Name>/<Name>Command.cs` (or `Query`)
2. `Features/<subdomain>/Domain/UseCases/<Name>/<Name>UseCase.cs`
3. New port needed? `Features/<subdomain>/Domain/Ports/I<Name>Port.cs` + adapter in `Common/Infra` + fake in `TestHelpers`
4. Unit tests in `tests/UnitTests` mirroring the folder path
5. Domain event if other features must react

## 3. Feature API Structure

The feature's driving-adapter layer lives in `Api/`:

```text
src/Features/<subdomain>/Api/
├── Endpoints/
│   └── <Endpoint>/                # e.g. Signup/
│       ├── <Name>Endpoint.cs
│       ├── <Name>EndpointRequest.cs
│       └── <Name>EndpointResponse.cs
├── ExceptionHandlers/             # feature exception → status + error code mapping
├── Schedules/<Name>/              # scheduled tasks (hosted services)
├── Services/                      # API-side shared logic (e.g. cookie composition)
├── Mappers/                       # transport ↔ domain enum/value mapping
├── Listeners/                     # integration-event consumers
└── Publishers/                    # domain-event → integration-event publishers
```

### 3.1 Endpoints

- One class per route, implementing the `IEndpoint` convention; request/response records local to the endpoint folder (full contract in [Architecture Overview §6](02-architecture-overview.md#6-endpoint-contract-repr)).
- Request fields carry validation attributes for transport shape; business invariants remain in the domain.
- Responses are explicit API contracts: map domain results field by field — never return domain or database types directly.

### 3.2 Scheduled Tasks

- Time-triggered work lives under `Features/<subdomain>/Api/Schedules/<Name>/` as `<Name>ScheduledTask`, delegating to a use case exactly like an endpoint does.
- Group only methods sharing the same dependency set; otherwise split.

**Why:** scheduled tasks are driving adapters — same translation-only rule as endpoints; grouping by dependencies avoids bloated scheduler classes.

### 3.3 API Services

- Shared API-side logic that several endpoints need (e.g. composing authentication cookies onto a response).
- Never business logic — if a decision depends on business rules, it belongs in the domain.

### 3.4 New Endpoint Checklist

1. `Features/<subdomain>/Api/Endpoints/<Name>/<Name>EndpointRequest.cs` — record, validation attributes on every field
2. `<Name>EndpointResponse.cs` — record with a `From(<domain type>)` factory
3. `<Name>Endpoint.cs` — route, explicit authorization, one use-case call
4. Web integration test in `tests/IntegrationTests` (status, payload, authorization)
5. New exception surfaced? Extend the feature exception handler + its handler tests

## 4. Common Modules

Shared, cross-feature code lives under `src/Common/`, split by layer:

```text
src/Common/
├── Api/                           # cross-cutting framework concerns
│   ├── Endpoints/                 # IEndpoint convention + discovery
│   ├── ExceptionHandling/         # handler chain, ErrorCode contract, ProblemDetails
│   ├── Security/                  # authentication setup, fallback policy, policies
│   ├── ServiceRegistration/       # use-case convention registration, adapter bindings
│   ├── Responses/                 # shared response bases (e.g. paged response)
│   ├── Validation/                # custom validation attributes
│   └── Properties/                # API-side options records
├── Domain/                        # shared domain abstractions
│   ├── Events/                    # IEvent, IIntegrationEvent marker interfaces
│   ├── Exceptions/                # DomainException base type
│   ├── Pagination/                # PageNumber, PageSize, PageQuery<T>, PageResult<T>…
│   ├── Ports/                     # IEventPublisherPort, ITimeProviderPort,
│   │                              #   IRandomGeneratorPort, IPasswordEncoderPort
│   └── ValueObjects/              # shared value objects (e.g. Username, Role)
├── Infra/                         # ALL driven adapters + technical config (centralized)
│   ├── Adapters/                  # port implementations: UserRepository, TimeProvider,
│   │                              #   RandomGenerator, EventPublisher, JwtTokenClaimsCodec…
│   ├── Database/
│   │   ├── AppDbContext.cs
│   │   ├── Entities/              # <Name>DbEntity — mutable persistence classes
│   │   ├── EntityConfigurations/  # IEntityTypeConfiguration<T> mappings
│   │   └── Logging/               # query logging, SQL commenting
│   ├── Configurations/            # technical wiring (interceptors, listeners)
│   ├── Logging/                   # audit sink, structured log formatting
│   ├── Mappers/                   # value-object ↔ primitive mapping helpers
│   ├── Properties/                # infrastructure options records (e.g. JwtProperties)
│   └── Tracing/                   # trace-id helpers
└── Utils/                         # generic, domain-agnostic helpers
```

### 4.1 Infrastructure is centralized

Adapters, database entities, and the `DbContext` live under `Common/Infra`, **not** inside each feature. A feature declares its ports in `Features/<subdomain>/Domain/Ports/`; the adapters implementing them sit together in `Common/Infra/Adapters/`.

- Adapters map database entities ↔ domain models **both ways** and translate infrastructure exceptions into domain exceptions. Details in [Data Persistence and Migrations](08-data-persistence-and-migrations.md).

**Why centralized:** driven adapters share heavy machinery (the `DbContext`, mapping helpers, logging); centralizing avoids N copies of it and keeps the *domain* the only per-feature axis. The boundary that matters — the domain sees only ports — holds regardless of where the adapters sit.

### 4.2 Module Responsibilities

| Module | Owns | Must not contain |
|---|---|---|
| `Common/Api` | host composition, security, exception handling, serialization, endpoint conventions | business logic |
| `Common/Domain` | shared domain abstractions (base exception, events, pagination, shared ports/VOs) | feature business entities |
| `Common/Infra` | all driven adapters + technical config | business decisions; API concerns |
| `Common/Utils` | generic, dependency-light helpers | domain helper logic (that's `Common/Domain`) |

## 5. Dependency Rules Summary

- `Features/<subdomain>/Domain` may depend on: the base class library, `System.Collections.Immutable`, `Common/Domain`, `Common/Utils`. Nothing else.
- `Features/<subdomain>/Api` and `Common/Infra` depend inward on domain (their own feature's and `Common/Domain`).
- `Common/Domain` may depend on the base library and `Common/Utils` only; it must not contain feature entities.
- Features must not depend on each other (except `Audit` consuming integration events).
- Domain code performs no direct I/O and reads no ambient state; all I/O is a port implemented by an adapter.

Enforcement details: [Architecture Overview §3](02-architecture-overview.md#3-dependency-direction-rules).

## 6. Naming Conventions (summary table)

| Concept | Pattern | Kind | Location |
|---|---|---|---|
| Use case | `<Name>UseCase`, one `Handle` | class | `Features/<subdomain>/Domain/UseCases/<Name>/` |
| Use case input | `<Name>Command` / `<Name>Query` | record | same folder as its use case |
| Port | `I<Name>Port` | interface | `Features/<subdomain>/Domain/Ports/` or `Common/Domain/Ports/` |
| Adapter | port name minus the `Port` suffix (`IUserRepositoryPort` → `UserRepository`); when the tech isn't obvious from the port, prefix it (`SpringSecurityPasswordEncoder`) | class | `Common/Infra/Adapters/` |
| Endpoint | `<Name>Endpoint` : `IEndpoint` | class | `Features/<subdomain>/Api/Endpoints/<Name>/` |
| Transport models | `<Name>EndpointRequest` / `<Name>EndpointResponse` | record | endpoint's folder |
| Database entity | `<Name>DbEntity` | mutable class | `Common/Infra/Database/Entities/` |
| Domain entity | noun (`User`) | record | `Features/<subdomain>/Domain/Entities/` |
| Value object | noun (`Username`) | record/enum | `Features/<subdomain>/Domain/ValueObjects/` or `Common/Domain/ValueObjects/` |
| Domain event | `<Fact>Event` : `IEvent`, past tense | record | `Features/<subdomain>/Domain/Events/` |
| Integration event | `<Fact>IntegrationEvent` : `IIntegrationEvent` | record | `Features/<subdomain>/IntegrationEvents/` |
| Exception | `<Name>Exception` : `DomainException` | class | `Features/<subdomain>/Domain/Exceptions/` |
| Error codes | enum implementing `IErrorCode` | enum | `Features/<subdomain>/Api/ExceptionHandlers/` |
| Scheduled task | `<Name>ScheduledTask` | class | `Features/<subdomain>/Api/Schedules/<Name>/` |
| Mapper | `<Name>Mapper` | class | `Features/<subdomain>/Api/Mappers/` or `Common/Infra/Mappers/` |
| Test classes | `*UnitTests`, `*IntegrationTests`, `*ApplicationTests`, `*ContractTests`, `*RulesUnitTests` | class | see [Testing Platform](05-testing-platform.md) |

**Enforced by:** the `NamingConventionRulesUnitTests` classes per area, `UseCaseRulesUnitTests`, `EndpointConventionRulesUnitTests`, `DbEntityRulesUnitTests`.

This table covers *what things are named and where they live*. For *how the C# is written* — casing, `record`/`sealed`/property idioms, the async rulebook (`Task` vs `await` vs sync, no `async` on interfaces, `CancellationToken`), and DI lifetimes — see [C# Language and Async Conventions](14-csharp-language-and-async-conventions.md).

## 7. Do / Do Not

✅ Do: create the full folder skeleton for a new feature even if some folders start empty — placement questions answered once.
✅ Do: keep one type per file, named identically.
❌ Do not: share transport models "for DRY" — duplication at boundaries is cheaper than coupling.
❌ Do not: expose `*DbEntity` types outside `Common/Infra`.
❌ Do not: put business branches in API services, mappers, or adapters.

## Navigation

⬅️ Previous: [Architecture Overview](02-architecture-overview.md) · ➡️ Next: [Project Bootstrap Checklist](04-project-bootstrap-checklist.md)
