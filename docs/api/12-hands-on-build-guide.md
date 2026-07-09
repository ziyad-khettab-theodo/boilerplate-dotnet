# Hands-On Build Guide

This guide constructs the entire platform **by hand**: every file, what each line does, how to verify it works, and how to break it on purpose to watch the gate fire. Each step cites the normative doc it implements — this guide is the *how*, those docs are the *law*.

The order is deliberately **code first, gates second**: you build a small but real end-to-end slice, test it, and only then layer the quality gates on top — so every gate has existing code to bite on, and you can tinker (violate a rule, watch the failure, fix it) instead of taking the gate on faith.

How to use it:

- Work phase by phase; **do not skip verify blocks** — they are the point.
- Type the files rather than pasting them; the friction is where the learning happens.
- When a tool's exact flag or option differs from what's written here (packages evolve), trust the tool's `--help` and current README, make it work, then update this guide in the same commit — the guide must never drift from reality.
- After each phase, update the [Feature Matrix](11-feature-matrix.md) Status column and commit.

**What you'll build:**

| Phase | Outcome |
|-------|---------|
| 1 Project essentials | Toolchain pinned; one buildable web project that serves hello-world. |
| 2 First end-to-end endpoint | `GET /api/users` flowing endpoint → use case → port → in-memory adapter. |
| 3 Testing the slice | A unit test and an integration test over that endpoint. |
| 4 Quality gates | Formatting, analyzers, architecture tests, coverage + mutation, `./validate`. |
| 5 Platform hardening | Deny-by-default security, error contract, strict JSON, health/OpenAPI/telemetry. |
| 6 Local stack | Docker Compose: PostgreSQL, observability; app containerized. |
| 7 CI/CD | GitHub Actions mirroring `./validate`, branch protection, Renovate. |
| 8 Persistence and features | Swap in-memory for PostgreSQL behind the same port; build the signup write path. |

> **Hexagonal in one minute.** The **domain** (business rules) sits at the center and depends on *nothing* outward. When it needs the outside world — a database, a clock, an email sender — it declares an interface called a **port** and works only against that. A **port** is a demand ("I need to load users"); an **adapter** is a concrete answer ("…from PostgreSQL"). Ports the domain *calls out to* (database, clock) are **driven** (outbound); the ones that *call into* the domain (HTTP endpoints) are **driving** (inbound). The single rule that makes it work: dependencies point **inward** — API and Infra know the domain, the domain knows neither. That's why in Phase 2 you can back an endpoint with an in-memory list today and PostgreSQL in Phase 8 *without touching the domain*. ([doc 02 §3](02-architecture-overview.md#3-dependency-direction-rules).)
>
> ```text
>        driving (inbound)                    driven (outbound)
>   HTTP ─► [Endpoint] ─► [Use case] ─► (IPort) ◄─ [Adapter] ─► DB / clock / …
>                            └─────── domain ───────┘   infra
>                         depends on nothing outward
> ```

---

## Phase 1 — Project Essentials

> **Concept primer.** A **`.csproj`** is a project: one buildable unit producing one assembly (a `.dll`), declaring its package references in a few lines of XML. A **solution** (`.slnx`) lists the projects that belong together. The whole application is **one project**; `Features/` and `Common/` are folders inside it, and the hexagonal layers are enforced by architecture tests, not assembly boundaries (see [Architecture Overview §3](02-architecture-overview.md#3-dependency-direction-rules)). The **SDK** (`dotnet`) builds, runs, and tests everything from the CLI.

### 1.0 Install the toolchain

Everything below needs the **.NET 10 SDK** (`dotnet` — builds/runs/tests) and **Docker** with Compose v2 (local database + observability stack, from phase 6). Install both before touching a file; if `dotnet` isn't found, nothing else in this guide runs.

**.NET 10 SDK:**

- **macOS** — `brew install --cask dotnet-sdk`, or the official installer (Arm64 for Apple silicon, x64 for Intel) from `https://dotnet.microsoft.com/download/dotnet/10.0`.
- **Linux** — your distro's package feed or the official `dotnet-install.sh` script (see the same download page).
- **Windows** — `winget install Microsoft.DotNet.SDK.10` or the installer.

**Docker:** Docker Desktop (macOS/Windows) or Docker Engine + Compose v2 (Linux).

**Verify:**

```bash
dotnet --version      # → 10.x.x
dotnet --list-sdks    # confirms a 10.x SDK is present
docker --version && docker compose version
```

Gotcha: if `dotnet` reports *command not found* right after the official installer, it's a `PATH` issue — the installer puts the SDK at `/usr/local/share/dotnet` (macOS); open a new shell, or ensure that directory is on `PATH`. Homebrew links `dotnet` onto `PATH` for you. The exact patch version doesn't matter yet — `global.json` (step 1.2) pins it next, and any installed 10.x that satisfies the pin is fine.

### 1.1 Repo hygiene

`.gitattributes` (repo root) — normalize line endings to LF; Windows scripts keep CRLF:

```text
* text=auto eol=lf
*.cmd eol=crlf
*.bat eol=crlf
```

### 1.2 Pin the toolchain

`api/global.json` — which installed SDK builds this repo, and which test runner `dotnet test` uses:

```json
{
  "sdk": { "version": "10.0.300", "rollForward": "latestFeature" },
  "test": { "runner": "Microsoft.Testing.Platform" }
}
```

Set `version` to the SDK you have (`dotnet --version`) or a floor at/below it. `rollForward: latestFeature` then accepts that feature band **or any higher one**, so a machine with `10.0.301` — or a newer `10.0.4xx` later — satisfies the pin without editing this file; it only fails if no installed 10.x SDK reaches the floor. (Example: floor `10.0.300` + installed `10.0.301` → resolves to `10.0.301`.)

### 1.3 Solution and the application project

```bash
cd api
dotnet new sln --name Theodo.DotnetBoilerplate      # solution file (see flags below)
dotnet new web -n Theodo.DotnetBoilerplate -o src   # the single application project
dotnet sln add src                                  # register the project in the solution
```

What each argument does:

- `dotnet new sln --name Theodo.DotnetBoilerplate` — creates an (empty) solution; `--name` sets its filename, so you get `Theodo.DotnetBoilerplate.slnx` (`.slnx` is the .NET 10 default solution format).
- `dotnet new web` — scaffolds the ASP.NET Core **empty web** template (just `Program.cs`, `appsettings*.json`, `Properties/launchSettings.json`, and the `.csproj` — no controllers, and **no `.http` file**; that ships only with the `webapi` template). `-n` / `--name` sets the project name, which becomes the assembly name, the root namespace, and the `.csproj` filename in one go; `-o` / `--output` is the folder to generate into (`src`).
- `dotnet sln add src` — adds the project found under `src/` to the solution, so `dotnet build` / `test` / `run` at the solution level include it.

One project holds the entire application; you'll grow `src/Features/` and `src/Common/` as folders inside it ([doc 03 §1](03-project-structure-and-conventions.md#1-repository-and-solution-layout)). The layer boundaries aren't drawn by project references — they're enforced by the architecture-test suite you add in phase 4.

**Verify:** `dotnet build` succeeds; `dotnet run --project src --urls http://localhost:8080` serves the template's hello-world.

---

## Phase 2 — First End-to-End Endpoint

Goal: `GET /api/users` flowing **endpoint → use case → port → adapter**, with the folder taxonomy from [doc 03](03-project-structure-and-conventions.md) built as you go. You'll build the same port **two ways** — an in-memory adapter *and* a real PostgreSQL adapter (§2.4) — and swap between them with one line. Seeing both behind one interface is the hexagonal point made concrete: the domain and endpoint don't change when the storage does. (Language rules referenced here — records, async, DI — are catalogued in [C# Language and Async Conventions](14-csharp-language-and-async-conventions.md); §2.0 teaches them inline.)

### 2.0 The C# you'll meet in this phase

> **Concept primer.** This phase introduces most of the C# you'll use everywhere. Skim it now; each item below is used in the code that follows. (Coming from Java? See the [casing and idiom map in doc 13 §5](13-guide-for-java-spring-developers.md#51-casing-reference).)

**Types and immutability**

- **File-scoped `namespace`** — `namespace X.Y.Z;` at the top means "everything in this file lives in `X.Y.Z`" (like a Java `package`, but the folder path isn't compiler-enforced — convention keeps them matched). One public type per file, file named after the type.
- **`record`** — a class the compiler gives **value equality**: two records with equal contents are `==`-equal (a plain `class` compares by reference). Domain types are records so equality and copying (`with`) come for free.
- **`sealed`** — cannot be subclassed (Java's `final class`). The default here unless a type is designed for inheritance.
- **Properties** — `public string Value { get; }` is a *property* (a get/set method pair that reads like a field), not a field. Forms: `{ get; }` set once in the constructor · `{ get; init; }` set only during construction (object-initializer or `with`) · `{ get; set; }` mutable · `=> expr` computed, no storage.
- **`required`** — the compiler forces every construction site to set the member, so there are no half-built objects (`new User { Id = ..., Username = ... }` won't compile if either is missing).
- **`Guid`** — a 128-bit identifier, the .NET equivalent of `java.util.UUID`. Preferred over `string` for ids.
- **`interface`** — same as Java; here it's how a **port** is declared. By convention port interfaces are `I`-prefixed (`IUserRepositoryPort`).

**Collections and generics**

- **Generics `<T>`** — `ImmutableList<User>` is "an immutable list of `User`" (like Java's `List<User>`). Domain signatures use the immutable collection types from `System.Collections.Immutable`.

**Namespaces and `using`** — the project has **implicit usings** on (the `dotnet new web` template enables it, and Phase 4 keeps it enforced), so common namespaces — `System`, `System.Linq`, `System.Threading.Tasks`, the ASP.NET Core builder/HTTP types — are imported for you; you won't write `using` lines for `Guid`, `Task`, `MapGet`, or LINQ. Two things this phase needs are **not** in that set and need an explicit `using`: `System.Collections.Immutable` (for `ImmutableList<T>`) and `Microsoft.AspNetCore.Http.HttpResults` (for the `Ok<T>` return type in §2.3). You also add a `using` for any type you reference from another namespace (e.g. the port file references `User`). Your IDE adds all of these automatically — put the cursor on the red type and press `Alt+Enter` (Rider/VS).

**Asynchronous code**

- **`Task<T>`** — a promise of a future `T`, like Java's `CompletableFuture<T>`. I/O-bound methods return `Task<T>`; `async`/`await` unwrap them without blocking a thread. A method that has no real async work yet returns a completed task via `Task.FromResult(value)`.
- **`CancellationToken`** — a cooperative "stop early" signal. When an HTTP client disconnects (or a test tears down), ASP.NET flags the token, and long operations can check it and abort instead of wasting work. It threads through every async layer (endpoint → use case → port → adapter) so cancellation reaches the database call. You rarely inspect it yourself — you just **accept one parameter and pass it down**. Three sources you'll see: the framework-supplied parameter (production), `CancellationToken.None` ("never cancels", used in unit tests), and `TestContext.Current.CancellationToken` (integration tests). Always name the parameter `cancellationToken`.

**Concise syntax**

- **Primary constructor** — `class GetUsersUseCase(IUserRepositoryPort userRepository)` declares a constructor parameter usable directly in the body; the go-to for dependency injection (no boilerplate field + assignment).
- **Expression-bodied member** — `=> expr` is shorthand for a method/property whose body is a single expression.
- **Collection expression + target-typed `new()`** — `[a, b]` builds a collection; `new() { ... }` infers the type from the left-hand side, so `ImmutableList<User> x = [ new() { Id = ... } ]` needs no repeated type names.

**Wiring (explained in detail where it appears in §2.3)**

- **Dependency injection (DI) + lifetimes** — the framework constructs your objects and passes ("injects") their dependencies. You register each: `AddScoped` = one instance per HTTP request (use cases, and anything touching a database), `AddSingleton` = one for the app's lifetime (safe for the stateless in-memory adapter now; the EF adapter in Phase 8 **must** be scoped because a `DbContext` is per-request), `AddTransient` = a fresh one each time.
- **Minimal API** — `app.MapGet("/users", Handle)` routes a URL to a method; the method returns an `IResult`, and `TypedResults.Ok(x)` is a strongly-typed `200 OK` carrying `x`.
- **LINQ** — `Select` (map), `Where` (filter), `ToImmutableList()` (materialize). Java Streams without the `.stream()`/`.collect()` ceremony.
- **REPR** = **R**equest–**E**nd**P**oint–**R**esponse: one class per route, its request and response types local to it, no shared fat controllers.

### 2.1 Domain: the hexagon's first cells

Immutable collections (`ImmutableList<T>`, `ImmutableHashSet<T>`, …) are the domain's allowed collection types. On .NET 10 they ship in the shared framework — no package to add.

`src/Common/Domain/ValueObjects/Username.cs` — a value object: validated at construction, impossible to hold an invalid value ([doc 03 §2.4](03-project-structure-and-conventions.md#24-entities-vs-value-objects)):

```csharp
namespace Theodo.DotnetBoilerplate.Common.Domain.ValueObjects;

public sealed record Username
{
    public string Value { get; }

    public Username(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 50)
            throw new ArgumentException("Username must be 1-50 non-blank characters", nameof(value));
        Value = value;
    }
}
```

`src/Features/Users/Domain/Entities/User.cs` — an entity: identity + data, as an immutable record:

```csharp
namespace Theodo.DotnetBoilerplate.Features.Users.Domain.Entities;

public sealed record User
{
    public required Guid Id { get; init; }
    public required Username Username { get; init; }
}
```

`src/Features/Users/Domain/Ports/IUserRepositoryPort.cs` — the domain's demand on the outside world (this file shows the two `using`s from §2.0 in place; other files omit them for brevity — let the IDE add them):

```csharp
using System.Collections.Immutable;                              // ImmutableList<T> — not an implicit using
using Theodo.DotnetBoilerplate.Features.Users.Domain.Entities;   // User lives in another namespace

namespace Theodo.DotnetBoilerplate.Features.Users.Domain.Ports;

public interface IUserRepositoryPort
{
    Task<ImmutableList<User>> FindAll(CancellationToken cancellationToken);
}
```

`src/Features/Users/Domain/UseCases/GetUsers/GetUsersQuery.cs` and `GetUsersUseCase.cs` — one operation, one class, one `Handle` ([doc 02 §5](02-architecture-overview.md#5-use-case-contract)):

```csharp
namespace Theodo.DotnetBoilerplate.Features.Users.Domain.UseCases.GetUsers;

// GetUsersQuery.cs — the use case's input (empty for now; filters/paging land here later)
public sealed record GetUsersQuery;

// GetUsersUseCase.cs — one operation, one public Handle method (doc 02 §5)
public sealed class GetUsersUseCase(IUserRepositoryPort userRepository)
{
    public Task<ImmutableList<User>> Handle(GetUsersQuery query, CancellationToken cancellationToken) =>
        userRepository.FindAll(cancellationToken);
}
```

The `(IUserRepositoryPort userRepository)` after the class name is a **primary constructor**: the use case declares that it *needs* a repository port, and DI (wired in §2.3) supplies one. The domain codes against the interface — it never knows whether the real object is the in-memory list or PostgreSQL.

(Thin today — pagination, filtering, and authorization context arrive later and will live *here*, not in the endpoint.)

**Verify it compiles:** `dotnet build` should succeed now — four domain files, no endpoint yet. If a type is red, it's a missing `using` (§2.0): let the IDE add it.

### 2.2 Infrastructure: the first adapter

Adapters are centralized under `Common/Infra` ([doc 03 §4.1](03-project-structure-and-conventions.md#41-infrastructure-is-centralized)). `src/Common/Infra/Adapters/InMemoryUserRepository.cs`:

```csharp
namespace Theodo.DotnetBoilerplate.Common.Infra.Adapters;

public sealed class InMemoryUserRepository : IUserRepositoryPort
{
    private static readonly ImmutableList<User> Seed =
    [
        new() { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Username = new Username("ada") },
        new() { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), Username = new Username("linus") },
    ];

    public Task<ImmutableList<User>> FindAll(CancellationToken cancellationToken) => Task.FromResult(Seed);
}
```

### 2.3 Api: the REPR convention and the endpoint

> **Heads-up.** The next few files (`AssemblyMarker`, `IEndpoint`, `EndpointDiscovery`, the two registrations) are **wiring plumbing you write once** and almost never touch again — they let every future endpoint and use case register itself just by existing, so you never edit a central list. They use a few advanced C# features on purpose; the important code (the *endpoint itself*) comes after, and reads plainly. Don't worry if the plumbing feels dense — a `**New C# here**` note explains each construct right below it.

`src/Common/Api/Endpoints/IEndpoint.cs` — the one-interface convention behind REPR ([doc 02 §6](02-architecture-overview.md#6-endpoint-contract-repr)):

```csharp
namespace Theodo.DotnetBoilerplate.Common.Api.Endpoints;

public interface IEndpoint
{
    static abstract void Map(IEndpointRouteBuilder app);
}
```

`src/Common/Utils/AssemblyMarker.cs` — an empty type used only as a stable **anchor** for "this application's assembly" when the plumbing below scans it by reflection (`typeof(AssemblyMarker).Assembly`). It carries no behavior; it exists so scanning code names the assembly through an intent-revealing type rather than borrowing an unrelated one. It's a technical helper, so it lives in `Common/Utils/` ([doc 03](03-project-structure-and-conventions.md)), not at the project root:

```csharp
namespace Theodo.DotnetBoilerplate.Common.Utils;

public sealed class AssemblyMarker;
```

`src/Common/Api/Endpoints/EndpointDiscovery.cs` — find every `IEndpoint` in the assembly and call its `Map` once at startup:

```csharp
public static class EndpointDiscovery
{
    public static void MapEndpoints(this IEndpointRouteBuilder app)
    {
        var endpointTypes = typeof(AssemblyMarker).Assembly.GetTypes()   // scan this app's assembly
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsAssignableTo(typeof(IEndpoint)));
        foreach (var type in endpointTypes)
            type.GetMethod(nameof(IEndpoint.Map))!.Invoke(null, [app]);
    }
}
```

`src/Common/Api/ServiceRegistration/UseCaseRegistration.cs` — use cases register by naming convention so the domain never carries DI attributes ([doc 02 §8](02-architecture-overview.md#8-dependency-injection-wiring-strategy)):

```csharp
public static class UseCaseRegistration
{
    public static IServiceCollection AddUseCases(this IServiceCollection services)
    {
        var useCases = typeof(AssemblyMarker).Assembly.GetTypes()   // same assembly anchor
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.Name.EndsWith("UseCase"));
        foreach (var useCase in useCases) services.AddScoped(useCase);
        return services;
    }
}
```

`AdapterRegistration.cs` beside it — explicit port→adapter bindings, the architecture visible in one place:

```csharp
public static IServiceCollection AddAdapters(this IServiceCollection services) =>
    services.AddSingleton<IUserRepositoryPort, InMemoryUserRepository>();
```

**New C# here (the plumbing above):**

- **`static abstract` interface member** (`IEndpoint`) — a contract satisfied by a *static* method on the implementer, so `Map` is called on the type itself, no instance needed. A modern (C# 11) feature; this is one of its few everyday uses.
- **Extension method** (`this IEndpointRouteBuilder app`) — the `this` on the first parameter lets you call `app.MapEndpoints()` as if `MapEndpoints` were defined on `app`. That's all `.AddUseCases()`/`.AddAdapters()` are too.
- **Reflection** (`typeof(AssemblyMarker).Assembly.GetTypes()`, `IsAssignableTo`, `GetMethod(...).Invoke(...)`) — inspecting types at runtime to *find* every endpoint/use case and call it, instead of maintaining a hand-written registry. This is the "register itself just by existing" trick.
- **Assembly anchor** (`typeof(AssemblyMarker).Assembly`) — `typeof(X).Assembly` returns the compiled assembly that contains `X`. `AssemblyMarker` is a meaning-free type whose only job is to name *this app's* assembly for the scans above — clearer than borrowing an unrelated type like `IEndpoint`, which would wrongly imply endpoints and use cases are related.
- **Property pattern** `t is { IsClass: true, IsAbstract: false }` — reads as "`t` is a non-abstract class"; concise matching on several properties at once.
- **Null-forgiving `!`** (`GetMethod(...)!`) — `GetMethod` *could* return null, but here the interface guarantees the method exists, so `!` tells the compiler "trust me, not null." Last resort; justified by that invariant.
- **`AddScoped` / `AddSingleton`** — the DI lifetimes from §2.0. Use cases are scoped (per request); the stateless in-memory adapter is a singleton for now (Phase 8 changes this).
- **Collection expression `[app]`** — a one-element array, the argument list for `Invoke`.

`src/Features/Users/Api/Endpoints/GetUsers/GetUsersEndpoint.cs` + `GetUsersEndpointResponse.cs` — a GET has no body, so this endpoint has no request record; the response record is still endpoint-local and mapped explicitly. `Ok<T>` needs `using Microsoft.AspNetCore.Http.HttpResults;` (§2.0):

```csharp
public sealed class GetUsersEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app.MapGet("/users", Handle);

    private static async Task<Ok<ImmutableList<GetUsersEndpointResponse>>> Handle(
        GetUsersUseCase useCase, CancellationToken cancellationToken)
    {
        var users = await useCase.Handle(new GetUsersQuery(), cancellationToken);
        return TypedResults.Ok(users.Select(GetUsersEndpointResponse.From).ToImmutableList());
    }
}

public sealed record GetUsersEndpointResponse
{
    public required Guid Id { get; init; }
    public required string Username { get; init; }

    public static GetUsersEndpointResponse From(User user) =>
        new() { Id = user.Id, Username = user.Username.Value };
}
```

Read `Handle` top to bottom: DI hands it the `GetUsersUseCase` and a `cancellationToken` (both are just method parameters — the framework supplies them); it `await`s the use case, then maps each domain `User` to the endpoint-local `GetUsersEndpointResponse` via the static `From` factory and returns a typed `200 OK`. The endpoint does **no** business logic — it translates between HTTP and the domain, nothing more. The response type is deliberately separate from `User` so the wire contract can't drift with the domain model. (`async`/`await`, `TypedResults.Ok`, and the LINQ `Select`/`ToImmutableList` are all in §2.0.)

`src/Program.cs` — deliberately minimal for now; each later phase adds one block:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddUseCases();
builder.Services.AddAdapters();

var app = builder.Build();
app.UsePathBase("/api");
app.UseRouting();
app.MapEndpoints();
app.Run();

public partial class Program;   // exposes Program to integration tests (phase 3)
```

`WebApplication.CreateBuilder` collects configuration and services (that's what `AddUseCases()`/`AddAdapters()` populate); `builder.Build()` produces the app; then the lines before `app.Run()` are the **middleware pipeline**, executed top to bottom per request — `UsePathBase("/api")` prefixes every route, `UseRouting()` matches the URL, `MapEndpoints()` (your discovery extension) attaches all the endpoints. `public partial class Program;` makes the otherwise-hidden top-level `Program` type public so Phase 3's `WebApplicationFactory<Program>` can boot the app in-process for integration tests; `partial` just means "this class may be defined across more than one place."

> ⚠️ **This endpoint is temporarily unauthenticated** — there is no security pipeline yet. Phase 5 turns on deny-by-default authorization, this exact endpoint will start returning 401, and you'll make its authorization explicit. That breakage is scheduled on purpose.

**Verify:**

```bash
dotnet run --project src --urls http://localhost:8080
curl http://localhost:8080/api/users     # → 200, JSON array with ada and linus
```

**Tinker:** trace the request through the four files in call order ([doc 01 §3](01-onboarding-guide.md#3-first-architecture-trace)). Then try to make `GetUsersUseCase` return usernames uppercased *from the endpoint* — notice how wrong it feels mechanically: the endpoint has no business seeing that rule. Put it in the use case, observe the endpoint didn't change. That's [doc 02 §4](02-architecture-overview.md#4-what-belongs-in-the-domain-vs-in-adapters) in your hands.

### 2.4 The same port, backed by PostgreSQL

> **Concept primer.** **EF Core** is .NET's ORM: it maps `*DbEntity` classes to tables and turns LINQ into SQL. You'll now write a *second* adapter for the exact same `IUserRepositoryPort` — one that talks to PostgreSQL — and swap the registration to it with **one line**. The domain, use case, and endpoint do not change. That swap *is* the hexagonal payoff; building it now (not in phase 8) lets you see both adapters side by side. Keep the mutable-`*DbEntity`-vs-immutable-domain split in mind ([doc 14 §2](14-csharp-language-and-async-conventions.md#2-types-and-immutability)).

**Step 1 — a local database.** Start the dependency stack you'll grow in phase 6. `.env` (repo root; `./init` renders it from `.env.template`):

```bash
POSTGRES_USER=app
POSTGRES_PASSWORD=app
POSTGRES_DB=app
```

`docker-compose.dependencies.yml` (repo root) — just the database for now; phase 6 adds pgAdmin and the observability stack:

```yaml
services:
  db:
    image: postgres:18   # pin by digest in real use (doc 10 §5)
    environment:
      POSTGRES_USER: ${POSTGRES_USER}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
      POSTGRES_DB: ${POSTGRES_DB}
    ports: ["127.0.0.1:5432:5432"]                 # localhost only
    volumes: ["pgdata:/var/lib/postgresql"]        # data survives restarts (18+ mounts the parent, not /data)
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U ${POSTGRES_USER}"]
      interval: 5s
      retries: 10

volumes:
  pgdata:
```

```bash
docker compose -f docker-compose.dependencies.yml up -d db   # → healthy
```

Point the app at it — `src/appsettings.Development.json`:

```json
{ "ConnectionStrings": { "Default": "Host=localhost;Port=5432;Username=app;Password=app;Database=app" } }
```

**Step 2 — EF Core + the persistence model.**

```bash
dotnet add src package Microsoft.EntityFrameworkCore
dotnet add src package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src package Microsoft.EntityFrameworkCore.Design   # migrations tooling
```

`src/Common/Infra/Database/Entities/UserDbEntity.cs` — a **mutable class** (the persistence-boundary exemption), with mapping both ways:

```csharp
public sealed class UserDbEntity
{
    public Guid Id { get; set; }
    public required string Username { get; set; }

    public User ToDomain() => new() { Id = Id, Username = new Username(Username) };
    public static UserDbEntity FromDomain(User user) => new() { Id = user.Id, Username = user.Username.Value };
}
```

`src/Common/Infra/Database/EntityConfigurations/UserDbEntityConfiguration.cs`:

```csharp
public sealed class UserDbEntityConfiguration : IEntityTypeConfiguration<UserDbEntity>
{
    public void Configure(EntityTypeBuilder<UserDbEntity> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Username).HasMaxLength(50).IsRequired();
        builder.HasIndex(u => u.Username).IsUnique();   // the DB guarantee signup relies on (§8.2)
    }
}
```

`src/Common/Infra/Database/AppDbContext.cs`:

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UserDbEntity> Users => Set<UserDbEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
```

Register the context in `Program.cs`:

```csharp
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
```

**Step 3 — the first migration** (with `db` running):

```bash
dotnet ef migrations add InitialCreate --project src   # generates Migrations/*.cs — READ it
dotnet ef database update --project src                # applies it
```

Read the generated file: a `users` table with a unique index on `username`. Surprising output means the configuration is wrong — fix that, never hand-edit the migration.

**Step 4 — the second adapter**, same port. `src/Common/Infra/Adapters/UserRepository.cs`:

```csharp
public sealed class UserRepository(AppDbContext dbContext) : IUserRepositoryPort
{
    public async Task<ImmutableList<User>> FindAll(CancellationToken cancellationToken) =>
        (await dbContext.Users.AsNoTracking().ToListAsync(cancellationToken))   // await → SQL runs, rows materialize
            .Select(entity => entity.ToDomain())                                // then map (sync, client-side)
            .ToImmutableList();
}
```

This is the first `async` adapter — it must `await` (to materialize rows) then map, so it carries the keyword; the in-memory one didn't ([why: doc 14 §3.2](14-csharp-language-and-async-conventions.md#32-async-is-an-implementation-detail)). `AsNoTracking()` because it's a read.

**Step 5 — swap, in one line.** In `AddAdapters()`, choose which adapter answers the port:

```csharp
public static IServiceCollection AddAdapters(this IServiceCollection services) =>
    services.AddScoped<IUserRepositoryPort, UserRepository>();          // PostgreSQL
    //    .AddSingleton<IUserRepositoryPort, InMemoryUserRepository>(); // ← swap to this to run with no database
```

Note the lifetime: the DB adapter is **`AddScoped`** because it depends on `AppDbContext` (per-request); the in-memory one was `AddSingleton` (stateless) — see [doc 14 §4](14-csharp-language-and-async-conventions.md#4-dependency-injection). **Both adapters stay in the codebase**: `InMemoryUserRepository` remains a valid, registered-by-one-line alternative — handy for running the app with no Docker, and its shape lives on as the test fake (§3).

**Verify:** with `UserRepository` wired and the migration applied, seed a row (`docker compose exec db psql -U app -c "insert into users values ('11111111-1111-1111-1111-111111111111','ada');"`) and `curl http://localhost:8080/api/users` → the **same JSON contract** as the in-memory version. **Tinker:** swap the registration back to `InMemoryUserRepository`, restart, `curl` again → identical response shape, no database needed. The endpoint and domain never changed. That invariance is the whole point of the port. *(Persistence details: [doc 08](08-data-persistence-and-migrations.md). Phase 8.1 makes this adapter production-grade with contract tests, Testcontainers, and query-count guards.)*

---

## Phase 3 — Testing the Slice

> **Concept primer.** Test projects are separate projects (separate assemblies) that reference the application project and a test framework. Ours split by **boot cost** ([doc 05 §2](05-testing-platform.md#2-test-categories-and-naming)): unit tests construct plain objects; integration tests boot an in-memory HTTP host (`WebApplicationFactory`) and exercise the real pipeline.

### 3.1 Projects

```bash
dotnet new install xunit.v3.templates    # once per machine
dotnet new xunit3 -n Theodo.DotnetBoilerplate.UnitTests -o tests/UnitTests
dotnet new xunit3 -n Theodo.DotnetBoilerplate.IntegrationTests -o tests/IntegrationTests
dotnet new classlib -n Theodo.DotnetBoilerplate.TestHelpers -o tests/TestHelpers
dotnet sln add tests/UnitTests tests/IntegrationTests tests/TestHelpers

dotnet add tests/TestHelpers reference src
dotnet add tests/UnitTests reference src tests/TestHelpers
dotnet add tests/IntegrationTests reference src tests/TestHelpers

dotnet add tests/UnitTests package AwesomeAssertions
dotnet add tests/IntegrationTests package AwesomeAssertions
dotnet add tests/IntegrationTests package Microsoft.AspNetCore.Mvc.Testing
```

The whole application is one project, so a unit-test project can technically *see* every type — "unit tests are only for domain code" ([doc 05 §3](05-testing-platform.md#3-unit-test-scope)) is therefore a convention enforced by an **architecture test** (phase 4.5), not by assembly boundaries. Discipline: keep `UnitTests` construction to domain types + fakes only.

### 3.2 The first fake and unit test

`tests/TestHelpers/Fakes/FakeUserRepository.cs` — a working in-memory implementation of the port, with test-only inspection helpers on the fake (never on the port — [doc 05 §4](05-testing-platform.md#4-fakes-over-mocks)):

```csharp
public sealed class FakeUserRepository : IUserRepositoryPort
{
    private readonly List<User> _users = [];

    public FakeUserRepository Containing(params User[] seed) { _users.AddRange(seed); return this; }

    public Task<ImmutableList<User>> FindAll(CancellationToken cancellationToken) =>
        Task.FromResult(_users.ToImmutableList());
}
```

`tests/TestHelpers/Fixtures/UserFixtures.cs` — irrelevant values defaulted, so tests state only what matters:

```csharp
public static class UserFixtures
{
    public static User AUser(string username = "ada", Guid? id = null) =>
        new() { Id = id ?? Guid.Parse("11111111-1111-1111-1111-111111111111"), Username = new Username(username) };
}
```

`tests/UnitTests/Features/Users/Domain/UseCases/GetUsers/GetUsersUseCaseUnitTests.cs` — note the folder mirrors the production path (including the `Domain/` segment), the suffix carries the category, and the single `// Act` marker ([doc 05 §9](05-testing-platform.md#9-the-act-pattern)):

```csharp
public class GetUsersUseCaseUnitTests
{
    [Fact]
    public async Task Returns_all_users_from_the_repository()
    {
        var useCase = new GetUsersUseCase(new FakeUserRepository().Containing(AUser("ada"), AUser("linus")));

        // Act
        var users = await useCase.Handle(new GetUsersQuery(), CancellationToken.None);

        users.Should().HaveCount(2);
        users.Select(u => u.Username.Value).Should().ContainInOrder("ada", "linus");
    }
}
```

### 3.3 The first integration test

`tests/IntegrationTests/Features/Users/GetUsersIntegrationTests.cs` — boots the real pipeline in memory:

```csharp
public class GetUsersIntegrationTests
{
    [Fact]
    public async Task Returns_the_seeded_users_as_json()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/users", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("ada").And.Contain("linus");
    }
}
```

**New C# here:**

- **`[Fact]`** — the xUnit attribute marking a method as a test. `[Theory]` (with data) comes later.
- **`params User[] seed`** (`FakeUserRepository.Containing`) — a variable-length argument list, so you call `Containing(a, b, c)`; the method returns `this` for a fluent `new FakeUserRepository().Containing(...)` chain.
- **`List<T>` in the fake** — the fake stores a mutable `List<User>` internally; that's fine because it's a private test detail. The *port* still returns `ImmutableList<User>` (via `.ToImmutableList()`), so the boundary contract is unchanged.
- **`Guid?` and `??`** (`UserFixtures`) — `Guid?` is a *nullable* `Guid` (can be absent); `id ?? default` means "use `id`, or the fallback if it's null." Lets a test override only the fields it cares about.
- **Fluent assertions `.Should()`** (AwesomeAssertions) — `users.Should().HaveCount(2)` reads as a sentence and produces a descriptive failure message; the repo bans raw `Assert.*` in behavioral tests.
- **`using var`** (integration test) — disposes the factory/client automatically at the end of the method (`IDisposable`, like Java's try-with-resources). The in-memory host is torn down deterministically.
- **`WebApplicationFactory<Program>`** — boots the whole app in-process (no network) so the test drives the real pipeline. It needs the `public partial class Program;` line you added in §2.3 to *see* the `Program` type.
- **`TestContext.Current.CancellationToken`** — the token xUnit v3 cancels when the test times out or the run is aborted; pass it to async calls (as §2.0 noted, this is the integration-test source of the token).

**Verify:** `dotnet test` → 2 tests green. (A `BaseWebIntegrationTests` base class extracts the factory/client plumbing as soon as a second integration test wants it — that's how the [doc 05 §7](05-testing-platform.md#7-test-infrastructure-building-blocks) building blocks are born: extracted from repetition, never speculative.)

**Tinker:** break the use case (return an empty list) → the unit test fails with a readable message *and* the integration test fails. Fix it; now break only the JSON mapping (rename a response property) → the unit test stays green, only the integration test catches it. Each test layer guards its own boundary.

---

## Phase 4 — Quality Gates (retrofit onto real code)

Now the payoff of code-first: every gate below immediately confronts the code you just wrote. Expect findings — fixing them is the exercise.

### 4.1 Repo-wide build policy

> **Concept primer.** **`Directory.Build.props`** is MSBuild's inheritance mechanism: properties in it apply to *every* project beneath it (the app project and all test projects). Policy lives there so no project can quietly opt out.

`api/Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>

    <!-- The null-safety gate: NRT on, every diagnostic fatal -->
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>

    <!-- Full analyzer set; style rules become build diagnostics -->
    <AnalysisLevel>latest-all</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <CodeAnalysisTreatWarningsAsErrors>true</CodeAnalysisTreatWarningsAsErrors>

    <!-- Reviewed, reproducible dependency graph -->
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>

    <!-- Vulnerability audit: high/critical advisories fail the build -->
    <NuGetAuditLevel>high</NuGetAuditLevel>
    <WarningsAsErrors>$(WarningsAsErrors);NU1903;NU1904</WarningsAsErrors>

    <!-- Reproducible binaries on CI -->
    <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
  </PropertyGroup>
</Project>
```

Then **delete** any now-duplicated properties from the project files (each `.csproj` shrinks to its SDK line + references + packages) and rebuild. The analyzers will almost certainly flag things in your phase-2/3 code — missing `sealed`, unused usings, naming. **Fix every finding**; this is the "existing code must follow the rules" moment you built this order for. *(Implements [toolchain §2](06-build-toolchain-and-quality-gates.md#2-zero-warning-compilation-and-analyzers).)*

### 4.2 Central package versions + lock files

Create `api/Directory.Packages.props` (`ManagePackageVersionsCentrally=true`), then migrate: move each `Version="…"` out of the project files into `<PackageVersion>` items. `dotnet restore` writes `packages.lock.json` per project — commit them.

**Tinker:** `dotnet restore --locked-mode` passes; bump any `PackageVersion` and run locked-mode again → it fails. The dependency graph is now a reviewed artifact ([toolchain §4](06-build-toolchain-and-quality-gates.md#4-dependency-integrity-and-vulnerability-gates)).

### 4.3 Formatting: CSharpier + hooks

```bash
dotnet new tool-manifest
dotnet tool install csharpier
dotnet tool install dotnet-reportgenerator-globaltool
dotnet tool install dotnet-stryker
dotnet csharpier format .        # one-time normalization commit
```

Silence the one analyzer rule that would fight the formatter — in `api/.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.IDE0055.severity = none
```

Install lefthook and the staged-file script — `lefthook.yml` (repo root):

```yaml
assert_lefthook_installed: true

pre-commit:
  commands:
    csharpier-staged:
      glob: "api/**/*.cs"
      run: ./scripts/csharpier-apply-staged {staged_files}
```

`scripts/csharpier-apply-staged` (`chmod +x`) — fully staged files get formatted and re-staged; **partially** staged files are only checked and block the commit (auto-formatting them would silently stage hunks you chose not to stage):

```bash
#!/bin/sh
set -eu
cd api
fully_staged=""; partially_staged=""
for f in "$@"; do
  rel="${f#api/}"
  if git diff --name-only | grep -qxF "$f"; then partially_staged="$partially_staged $rel"
  else fully_staged="$fully_staged $rel"; fi
done
[ -n "$fully_staged" ] && dotnet csharpier format $fully_staged && (cd .. && git add $fully_staged)
if [ -n "$partially_staged" ]; then
  dotnet csharpier check $partially_staged || {
    echo "unformatted partially-staged files — stage or unstage them fully and retry"; exit 1; }
fi
```

And the bootstrap script `init` (repo root, `chmod +x`) so a fresh clone gets tools + hooks in one idempotent, repo-local command:

```bash
#!/bin/sh
set -eu
cd "$(dirname "$0")"
[ -f .env ] || { [ -f .env.template ] && cp .env.template .env && echo "rendered .env"; }
(cd api && dotnet tool restore)
command -v lefthook >/dev/null 2>&1 && lefthook install || true
```

**Tinker:** mangle indentation in a file, commit it → the hook reformats and the commit contains clean code. Stage only *half* the file (`git add -p`), commit → blocked with the explanation. *(Implements [toolchain §2.2](06-build-toolchain-and-quality-gates.md#22-formatting-csharpier-owns-layout).)*

### 4.4 Analyzer packs + banned APIs

```bash
dotnet add src package SonarAnalyzer.CSharp
dotnet add src package Microsoft.CodeAnalysis.BannedApiAnalyzers
```

(Analyzer packages are build-time only — mark them `PrivateAssets="all"`.) Fix whatever Sonar finds in existing code.

**A note on scope.** `BannedSymbols.txt` is per-*project*, and the app is one project — so a symbol banned here is banned everywhere, including the adapters that legitimately need clocks and GUIDs. Use the project-wide `BannedSymbols.txt` only for things that are *never* acceptable anywhere (e.g. `System.DateTime.Now` in favor of UTC). The **domain-scoped** determinism rule — "no clocks/GUID/random *in the domain*" — is enforced instead by an ArchUnitNET rule in 4.5. A minimal universal ban:

`api/src/BannedSymbols.txt`:

```text
P:System.DateTime.Now;Use ITimeProviderPort in domain, DateTimeOffset.UtcNow in adapters
P:System.DateTime.Today;Use ITimeProviderPort in domain, DateTimeOffset.UtcNow in adapters
```

wired in `src/Theodo.DotnetBoilerplate.csproj`: `<AdditionalFiles Include="BannedSymbols.txt" />`.

### 4.5 Architecture tests — the layer boundaries

```bash
dotnet new xunit3 -n Theodo.DotnetBoilerplate.ArchitectureTests -o tests/ArchitectureTests
dotnet sln add tests/ArchitectureTests
dotnet add tests/ArchitectureTests reference src
dotnet add tests/ArchitectureTests package TngTech.ArchUnitNET.xUnitV3
```

This is where the hexagonal boundaries live (the single project can't enforce them by reference). The rules name the application assembly through the `AssemblyMarker` you already added in §2.3 (the same anchor the endpoint/use-case scans use). Write the load-bearing rules — `tests/ArchitectureTests/HexagonalArchitectureRulesUnitTests.cs`:

```csharp
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

public class HexagonalArchitectureRulesUnitTests
{
    private static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies(typeof(Theodo.DotnetBoilerplate.Common.Utils.AssemblyMarker).Assembly)
        .Build();

    [Fact]
    public void Domain_does_not_depend_on_framework_or_adapters() =>
        Types().That().ResideInNamespace(@".*\.Domain(\.|$)", true)
            .Should().NotDependOnAny(
                Types().That().ResideInNamespace("Microsoft.AspNetCore.*", true)
                    .Or().ResideInNamespace("Microsoft.EntityFrameworkCore.*", true)
                    .Or().ResideInNamespace(@".*\.Common\.Infra(\.|$)", true)
                    .Or().ResideInNamespace(@".*\.Api(\.|$)", true))
            .Because("the domain is the framework-free hexagon")
            .Check(Architecture);

    [Fact]
    public void Features_do_not_depend_on_each_other() =>
        // FeatureIsolation is a small test-owned helper (in this project) that discovers every
        // Features/<subdomain> namespace and builds one pairwise "must-not-depend-on" rule, so the
        // check grows automatically as features are added — no need to edit this test per feature.
        FeatureIsolation.Rule().Check(Architecture);

    [Fact]
    public void Use_cases_have_exactly_one_public_Handle_method() =>
        Classes().That().HaveNameEndingWith("UseCase")
            .Should().FollowCustomCondition(c =>
                c.GetMethodMembers().Count(m => m.Visibility == Visibility.Public && !m.IsConstructor()) == 1
                && c.GetMethodMembers().Any(m => m.Name.StartsWith("Handle(")),
                "have exactly one public method, named Handle",
                "use cases are one operation each")
            .Check(Architecture);
}
```

(The fluent API surface evolves — keep the *rule names* and *reasons* aligned with the [inventory](06-build-toolchain-and-quality-gates.md#6-architecture-test-inventory) and adapt the query syntax to the current ArchUnitNET README.) Also add the determinism rule (`DesignRulesUnitTests`: domain namespaces must not call `DateTime.UtcNow`/`Guid.NewGuid`/`new Random()`), the endpoint-convention rules, and the "unit tests only reference domain types" rule promised in phase 3. Grow the suite one rule at a time: **every convention you adopt from here on gets its rule the same day.**

**Tinker:** add `using Microsoft.EntityFrameworkCore;` and a `DbContext` field to `GetUsersUseCase` → the *domain-purity* arch test fails, naming the rule (it compiles — that's the point of the test tier). Remove it. Add `Guid.NewGuid()` to the use case → the determinism rule fails; the same call in `InMemoryUserRepository` passes. Move `GetUsersEndpoint.cs` out of its `Endpoints/GetUsers/` folder → the endpoint-placement rule fails. This is [doc 02 §3](02-architecture-overview.md#3-dependency-direction-rules) biting.

### 4.6 Coverage + mutation gates, and `./validate`

`validate` (repo root, `chmod +x`) — the single pre-push command, now assembling every gate in triage order ([toolchain §5](06-build-toolchain-and-quality-gates.md#5-command-intent)):

```bash
#!/bin/sh
set -eu
cd "$(dirname "$0")/api"

echo "── format"       && dotnet csharpier check .
echo "── build"        && dotnet build --configuration Release
echo "── architecture" && dotnet test tests/ArchitectureTests --no-build --configuration Release
echo "── unit"         && dotnet test tests/UnitTests --no-build --configuration Release \
                            -- --coverage --coverage-output-format cobertura
echo "── integration"  && dotnet test tests/IntegrationTests --no-build --configuration Release \
                            -- --coverage --coverage-output-format cobertura

echo "── coverage gate"
if grep -q "Theodo.DotnetBoilerplate" Theodo.DotnetBoilerplate.slnx; then LINE=100; BRANCH=100; MUT=100; else LINE=95; BRANCH=80; MUT=95; fi
dotnet reportgenerator \
  -reports:"tests/**/TestResults/**/*.cobertura.xml" -targetdir:artifacts/coverage \
  "-reporttypes:Html;TextSummary" \
  "minimumCoverageThresholds:lineCoverage=$LINE;branchCoverage=$BRANCH"
# Domain-only gate: same command with classfilters "+*.Domain.*" and a stricter threshold

echo "── mutation"     && dotnet stryker --break-at $MUT
echo "OK"
```

Notes: the coverage flags belong to the testing-platform collector (`dotnet test -- --help` lists them); **ReportGenerator is the gate** — it exits non-zero below threshold, and its `classfilters` scope the Domain-only gate by namespace ([toolchain §3](06-build-toolchain-and-quality-gates.md#3-coverage-and-mutation-thresholds)); the `grep` on the solution name implements the template-100%/client-baseline switch. Add `api/stryker-config.json` targeting the single project, with `test-runner: "mtp"`, `UnitTests` as the test project, `mutate` globs restricting mutation to the domain (`**/Features/**/Domain/**`, `**/Common/Domain/**`, `**/Common/Utils/**`), and thresholds `{ high: 95, low: 90, break: 95 }` — keep the `mtp` runner, the default mis-scores this stack.

**Tinker (the best one):** in `GetUsersUseCaseUnitTests`, delete the assertions but keep the call. Coverage stays **100%** — the line was executed! — but Stryker now reports surviving mutants: it mutated the use case and no test noticed. Restore the assertions, watch the mutants die. You now understand *why* both gates exist ([toolchain §3 primer](06-build-toolchain-and-quality-gates.md#3-coverage-and-mutation-thresholds)).

---

## Phase 5 — Platform Hardening

> **Concept primer.** ASP.NET Core processes each request through a **middleware pipeline** — an ordered list of components (`app.UseX()`) that each inspect/modify the request and response and decide whether to pass control on. **Order matters**: authentication must run before authorization, exception handling must wrap everything. Most hardening below is "register a service (`builder.Services.AddX()`) *and* add its middleware (`app.UseX()`) in the right place." `ProblemDetails` is the standard machine-readable error body ([RFC 9457](09-security-observability-and-error-handling.md#7-error-contract)); **options** are strongly-typed configuration validated at startup.

Each block below hardens the running app; add them to `Program.cs` one commit at a time, watching what each breaks.

### 5.1 Deny-by-default security

Register the services (before `builder.Build()`):

```csharp
builder.Services.AddAuthentication().AddJwtBearer();   // token read from cookie: configured with the auth feature
builder.Services.AddAuthorization(o =>
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// CORS from configuration, never wildcarded in a deployed env (doc 09 §3)
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Security:Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
```

Then wire the middleware, in this order, **before** `app.MapEndpoints()`:

```csharp
app.UseAuthentication();   // who are you?
app.UseAuthorization();    // are you allowed?
app.UseCors();
```

**Your phase-2 endpoint now returns 401.** That's the scheduled breakage: nothing declared its exposure, so the fallback secured it ([doc 02 §6.1](02-architecture-overview.md#61-authorization-rule)). Resolve it *explicitly*: `.RequireAuthorization()` on `/users` — and since no login exists yet, integration tests authenticate via a test authentication scheme registered in the test factory (`AuthenticationBuilder.AddScheme` with a handler that issues a test principal), while `curl` correctly gets 401 until the authentication feature lands in phase 8. Add the endpoint-authorization architecture rule now (every endpoint declares intent; `/public/` ⇔ `AllowAnonymous`).

### 5.2 Error contract

`AddProblemDetails()` + an ordered `IExceptionHandler` chain. `Common/Api/ExceptionHandling/` holds the `IErrorCode` contract (`public interface IErrorCode { string Code { get; } }`, feature enums implement it) and the terminal handler; feature handlers (phase 8) map their own exceptions and sit ahead of it. Handlers are **mapping tables, not logic** ([doc 09 §7.1](09-security-observability-and-error-handling.md#71-exception-handler-layering)):

```csharp
// Common/Api/ExceptionHandling/CatchAllExceptionHandler.cs — the terminal handler (registered last)
internal sealed class CatchAllExceptionHandler(IProblemDetailsService problemDetails,
    ILogger<CatchAllExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken cancellationToken)
    {
        logger.LogError(ex, "Unhandled exception");            // full detail to logs only
        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problemDetails.TryWriteAsync(new()
        {
            HttpContext = ctx,
            ProblemDetails = { Title = "errors.internal", Status = 500 },  // stable code; no internals in body
        });
    }
}
```

Register it in `Program.cs` — services first, then the middleware:

```csharp
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<CatchAllExceptionHandler>();  // feature handlers are added before this one
// ... after builder.Build():
app.UseExceptionHandler();
```

**New C# here:** `internal` = visible only inside this assembly (infrastructure not meant as a public contract); `ValueTask<bool>` = a `Task`-like return optimized for the common "completes synchronously" case; `ILogger<CatchAllExceptionHandler>` = a category-typed logger DI provides; the handler takes both dependencies via a primary constructor (§2.0).

A feature handler (phase 8) is the same shape but catches its own exception type, sets the mapped status, and uses its `errors.*` code — with an integration test asserting the exact ProblemDetails JSON. **Tinker:** throw from the use case → the response is a clean ProblemDetails with `title: "errors.internal"`, and the stack trace is in the console only. *(Implements [doc 09 §7](09-security-observability-and-error-handling.md#7-error-contract).)*

> **Why this works across `async`.** A domain exception thrown deep in an awaited call chain propagates back through every `await` **as if the code were synchronous** — the state machine re-throws it at the `await` point — so it reaches this handler untouched. That is why the handlers need no async-specific machinery. Two rules make it hold: never `async void` (its exceptions escape the caller and can crash the process), and never swallow with `.Result`/`.Wait()` (they wrap the real exception in `AggregateException`). Full async-exception rules: [doc 14 §3.4–3.5](14-csharp-language-and-async-conventions.md#34-banned--dangerous). The decision to use exceptions (not Result types) for domain failures is recorded in [doc 09 §7.2](09-security-observability-and-error-handling.md#72-decision-typed-exceptions-not-result-types).

### 5.3 Strict JSON + validated options

```csharp
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
builder.Services.AddValidation();   // .NET 10 built-in DataAnnotations validation for minimal APIs
```

Options records (`Common/Api/Properties/`) are validated and fail fast at boot:

```csharp
public sealed class SecurityOptions
{
    public const string Section = "Security";
    [Required] public required JwtOptions Jwt { get; init; }
    public bool Https { get; init; }
}
```

(`JwtOptions` is a nested options record — issuer, audience, signing key, lifetimes — introduced with the authentication feature in Phase 8.3; `SecurityOptions` just references it here.)

Bind and validate it at startup in `Program.cs`:

```csharp
builder.Services.AddOptions<SecurityOptions>()
    .BindConfiguration(SecurityOptions.Section)
    .ValidateDataAnnotations()
    .ValidateOnStart();          // missing/invalid setting → crash at boot, naming the option
```

**Tinker:** POST an unknown JSON field to any future endpoint → 400; delete a required config value → the app refuses to start with a message naming the missing key (never a 3 a.m. null downstream). *(Implements [doc 09 §8.3](09-security-observability-and-error-handling.md#83-openapi-and-serialization) and the production-override split in [doc 09 §9.1](09-security-observability-and-error-handling.md#91-example-production-overrides).)*

### 5.4 Health, OpenAPI, telemetry

- `AddHealthChecks()` + `MapHealthChecks("/public/health").AllowAnonymous()` — the path convention's first legitimate use.
- `AddOpenApi()` + Scalar UI mapped **only when** `OpenApi:Enabled` (Development default) — confirm the package's current UI route and pin it in config.
- OpenTelemetry: resource identity from `OTEL_SERVICE_NAME`/`APP_ENVIRONMENT`, ASP.NET Core + HTTP instrumentation, JSON console logs outside Development, `UseOtlpExporter()` when an endpoint is configured, `X-Trace-Id` response header middleware. *(Implements [doc 09 §8.4–8.6](09-security-observability-and-error-handling.md#84-structured-logging-and-trace-correlation).)*

**Verify (phase gate):** health = 200 anonymously · `/api/users` = 401 raw, 200 via test auth in integration tests · unknown JSON field = 400 · docs UI in Development only.

---

## Phase 6 — Local Stack

> **Concept primer.** **Docker Compose** describes a set of containers in one YAML file and starts them together. You already started the `db` service and `.env` in [§2.4](#24-the-same-port-backed-by-postgresql); here you **complete** the stack — add developer tooling (pgAdmin) and observability (Grafana), then containerize the API itself. A **profile** (`profiles: [localdev]`) marks a service opt-in, so it only starts when `COMPOSE_PROFILES` includes it.

**Step 1 — extend `.env` / `.env.template`** with the two values the rest of the stack needs (the `POSTGRES_*` vars are already there from §2.4):

```bash
API_PORT=8080
COMPOSE_PROFILES=localdev
```

**Step 2 — extend `docker-compose.dependencies.yml`** — add pgAdmin and the observability stack beside the `db` service you wrote in §2.4:

```yaml
  pgadmin:
    image: dpage/pgadmin4
    profiles: [localdev]                  # opt-in: only starts with COMPOSE_PROFILES=localdev
    ports: ["127.0.0.1:5050:80"]
    environment:
      PGADMIN_DEFAULT_EMAIL: dev@local
      PGADMIN_DEFAULT_PASSWORD: dev

  otel-lgtm:                              # Grafana all-in-one: logs, traces, metrics
    image: grafana/otel-lgtm
    profiles: [localdev]
    ports:
      - "127.0.0.1:3333:3000"             # Grafana UI
      - "127.0.0.1:4317:4317"             # OTLP gRPC
      - "127.0.0.1:4318:4318"             # OTLP HTTP
```

(The `db` service and the `pgdata` volume already exist from §2.4 — leave them as they are.)

**Step 3 — `docker-compose.yml`** — the API container, including the dependencies file:

```yaml
include:
  - docker-compose.dependencies.yml

services:
  api:
    build:
      context: .
      dockerfile: api/Dockerfile
    ports: ["127.0.0.1:${API_PORT}:8080"]
    environment:
      ConnectionStrings__Default: "Host=db;Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD};Database=${POSTGRES_DB}"
      OTEL_EXPORTER_OTLP_ENDPOINT: "http://otel-lgtm:4317"
    depends_on:
      db: { condition: service_healthy }   # wait for Postgres to pass its healthcheck first
```

**Step 4 — `api/Dockerfile`** — a **multi-stage** build (compile in the SDK image, ship only the runtime):

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish api/src -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN adduser --disabled-password --gecos "" appuser && apt-get update \
    && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
USER appuser                              # run as non-root
COPY --from=build /app .
HEALTHCHECK --interval=10s --timeout=3s --retries=5 \
  CMD curl -f http://localhost:8080/api/public/health || exit 1
ENTRYPOINT ["dotnet", "Theodo.DotnetBoilerplate.dll"]
```

**New here:** *multi-stage build* — the `sdk` image (large, has the compiler) produces the binaries; the final `aspnet` image (small, runtime-only) copies just `/app`, so the shipped image is lean and has no build tools. *non-root user* — a container security baseline. *`HEALTHCHECK`* — Docker periodically probes the endpoint and marks the container healthy/unhealthy (Compose's `depends_on: condition: service_healthy` relies on it). The health path is `/api/public/health` because of the `/api` path base + the `/public/` anonymous convention from §5.4.

Point the app's Development config at the local OTLP endpoint (`OTEL_EXPORTER_OTLP_ENDPOINT`, already set in Step 3).

**Verify:** `docker compose up -d` → all containers healthy (`docker compose ps`); hit `/api/users`, then open Grafana at `http://localhost:3333`, find that exact request's trace, and confirm its JSON log line is joined to the trace by `trace_id`. **Tinker:** stop `db` (`docker compose stop db`) and start the API → it should fail fast at boot with a clear connection error, not hang. *(Implements [doc 09 §8.7](09-security-observability-and-error-handling.md#87-local-observability-stack); pinning per [doc 10 §5](10-ci-cd-and-governance.md#5-supply-chain-pinning).)*

---

## Phase 7 — CI/CD

> **Concept primer.** **GitHub Actions** runs workflows (YAML in `.github/workflows/`) on events like "PR opened." A workflow has **jobs**; jobs have **steps** (each an action or a shell command). The rule that keeps CI honest: **CI runs exactly what `./validate` runs locally**, so a green local run predicts a green CI run. Actions are **pinned by commit SHA** (not a moving tag) so a compromised tag can't silently change what runs — a supply-chain safeguard ([doc 10 §5](10-ci-cd-and-governance.md#5-supply-chain-pinning)).

Build it in three commits, mirroring [doc 10](10-ci-cd-and-governance.md).

**Step 1 — the backend CI job (`.github/workflows/ci.yml`)** — the same stages as `./validate`, as jobs:

```yaml
name: ci
on:
  pull_request:
  push: { branches: [main] }

jobs:
  backend:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@<sha>              # pin every action by full commit SHA
      - uses: actions/setup-dotnet@<sha>
        with: { global-json-file: api/global.json }  # same SDK as local, from the pin
      - run: ./validate                            # one command: format → build → arch → unit → integration → gates
```

Because CI just calls `./validate`, every gate you add later (a new architecture rule, a coverage bump) lands in *both* local and CI in the same commit — they can't drift.

**Step 2 — topology with change detection.** A small `all_configure.yml` job inspects the PR's changed paths and outputs booleans; downstream jobs gate on them, so a **docs-only PR skips the backend** (and vice-versa). Sketch:

```yaml
jobs:
  configure:
    runs-on: ubuntu-latest
    outputs:
      backend: ${{ steps.filter.outputs.backend }}
      docs: ${{ steps.filter.outputs.docs }}
    steps:
      - uses: actions/checkout@<sha>
      - id: filter
        uses: dorny/paths-filter@<sha>
        with:
          filters: |
            backend: ['api/**']
            docs: ['docs/**']

  backend:
    needs: configure
    if: ${{ needs.configure.outputs.backend == 'true' }}
    uses: ./.github/workflows/ci.yml

  docs-checker:
    needs: configure
    if: ${{ needs.configure.outputs.docs == 'true' }}
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@<sha>
      - run: npx markdownlint-cli2 "docs/**/*.md"   # + a link checker over docs/

  end-marker:                                   # single required check the ruleset points at
    needs: [backend, docs-checker]
    if: always()
    runs-on: ubuntu-latest
    steps:
      - run: |
          [ "${{ contains(needs.*.result, 'failure') }}" = "false" ] || exit 1
```

The **"end marker"** job is the aggregate: it's the *one* status the branch ruleset requires, so skipped-but-not-failed jobs (the backend on a docs PR) still let the marker go green. Add SAST (e.g. OpenGrep) as another gated job alongside `docs-checker`.

**Step 3 — governance.** `default_branch_ruleset.json` (imported in repo settings): require 1 approval, linear history, squash/rebase only, and **the end-marker as the sole required check**. Add **Renovate** (`.ci/renovate/`) for dependency PRs: a 5-day minimum release age, grouped non-major updates, patch-level automerge. Add a scheduled daily dependency-audit workflow.

**Verify:** open a PR with a formatting violation → only the backend job (its format stage) is red, the end-marker is red, merge is blocked. Open a docs-only PR → backend is skipped, docs-checker runs, end-marker is green. **Tinker:** change an action's pin from a SHA to `@v4` → note it still runs, but you've reopened the supply-chain hole the SHA pin closed.

---

## Phase 8 — Persistence and Real Features

> **Concept primer.** You already built the PostgreSQL adapter, `AppDbContext`, `UserDbEntity`, and the first migration in [§2.4](#24-the-same-port-backed-by-postgresql). Phase 8 makes that adapter **production-grade** and adds the first **write** path. Two concepts here: a **contract test** — an abstract test defining a port's expected behavior *once* and run against every implementation (fake and real), so they can't diverge; and **Testcontainers** — a throwaway PostgreSQL in Docker so the real adapter is tested against real SQL, not a mock.

### 8.1 Harden the PostgreSQL adapter

The adapter works (§2.4). Now prove it and guard it — test-first, against real SQL.

**Step 1 — pin the behavior in a contract test** (`tests/TestHelpers/Contracts/UserRepositoryPortContractTests.cs`). It states what *any* `IUserRepositoryPort` must do, with an abstract seeding hook each implementation fills in:

```csharp
public abstract class UserRepositoryPortContractTests
{
    protected abstract IUserRepositoryPort Repository { get; }
    protected abstract Task GivenExistingUsers(params User[] users);

    [Fact]
    public async Task FindAll_returns_every_stored_user()
    {
        await GivenExistingUsers(AUser("ada"), AUser("linus"));

        // Act
        var users = await Repository.FindAll(CancellationToken.None);

        users.Select(u => u.Username.Value).Should().BeEquivalentTo("ada", "linus");
    }
}
```

Run it against the fake immediately (`tests/UnitTests/.../FakeUserRepositoryContractTests.cs`):

```csharp
public sealed class FakeUserRepositoryContractTests : UserRepositoryPortContractTests
{
    private readonly FakeUserRepository repository = new();
    protected override IUserRepositoryPort Repository => repository;
    protected override Task GivenExistingUsers(params User[] users)
    {
        repository.Containing(users);
        return Task.CompletedTask;
    }
}
```

The real adapter (`UserRepository`), `AppDbContext`, and `UserDbEntity` already exist from [§2.4](#24-the-same-port-backed-by-postgresql) — this section proves and guards them, it doesn't rebuild them.

**Step 2 — run the same contract against the real adapter** via Testcontainers, which starts a throwaway PostgreSQL in Docker. Package `Testcontainers.PostgreSql`; with xUnit v3, share one container across the assembly:

```csharp
[assembly: AssemblyFixture(typeof(PostgresFixture))]

public sealed class PostgresFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; } =
        new PostgreSqlBuilder().WithImage("postgres:18").Build();   // match the compose image

    public async ValueTask InitializeAsync() => await Container.StartAsync();
    public async ValueTask DisposeAsync() => await Container.DisposeAsync();
}
```

```csharp
public sealed class UserRepositoryContractTests : UserRepositoryPortContractTests, IAsyncLifetime
{
    private readonly AppDbContext dbContext;

    public UserRepositoryContractTests(PostgresFixture postgres)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.Container.GetConnectionString()).Options;
        dbContext = new AppDbContext(options);
    }

    public async ValueTask InitializeAsync() => await dbContext.Database.MigrateAsync();
    public async ValueTask DisposeAsync() => await dbContext.DisposeAsync();

    protected override IUserRepositoryPort Repository => new UserRepository(dbContext);
    protected override async Task GivenExistingUsers(params User[] users)
    {
        dbContext.Users.AddRange(users.Select(UserDbEntity.FromDomain));
        await dbContext.SaveChangesAsync();
    }
}
```

The same assertions now prove the real SQL adapter behaves identically to the fake — one contract, two implementations, no drift.

**Step 3 — add the guards:**

- **query-count guards** — the repo's `[AssertQueryCount]` + `[ExpectedQueries(n)]` attributes, backed by an EF command interceptor that counts executed commands, so an accidental N+1 fails the test ([doc 05](05-testing-platform.md#5-port-contract-tests)).
- a **schema-drift test** — `dbContext.Database.HasPendingModelChanges().Should().BeFalse();` fails if the model changed without a new migration.
- a **destructive-migration CI guard** (doc 10) that blocks a migration dropping a column without review.

(The registration swap and the `AddScoped` lifetime were done in [§2.4](#24-the-same-port-backed-by-postgresql); nothing to change here.)

**Verify:** `dotnet test` → the contract passes against **both** `FakeUserRepository` and the real `UserRepository`. **Tinker:** delete the unique index from the configuration and re-add the migration → the schema-drift test goes red until model and migrations agree again. Then break `UserRepository.FindAll` (return an empty list) → the real-adapter contract test fails while the fake's stays green, pinpointing which implementation broke.

### 8.2 First write path: signup

Domain-first — build inward-out, letting each gate teach. `POST /auth/public/signup` creating a user.

**Step 1 — grow the domain.** The write needs a "to-be-created" shape (`NewUser`), a failure type, and a command.

```csharp
// Features/Users/Domain/ValueObjects/NewUser.cs — validated data for a user that doesn't exist yet
public sealed record NewUser
{
    public required Guid Id { get; init; }
    public required Username Username { get; init; }
    public required string PasswordHash { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

// Features/Users/Domain/Exceptions/UsernameAlreadyExistsException.cs
public sealed class UsernameAlreadyExistsException(Username username)
    : DomainException($"Username '{username.Value}' already exists");

// Features/Users/Domain/UseCases/Signup/SignupCommand.cs
public sealed record SignupCommand
{
    public required string Username { get; init; }
    public required string Password { get; init; }
}
```

**Step 2 — the use case, and why the determinism rule forces the ports.** The domain may not read a clock, generate a GUID, or hash a password itself (§4.5 forbids ambient state). So each of those becomes an injected port — the rule *designs the seams for you*:

```csharp
public sealed class SignupUseCase(
    IUserRepositoryPort userRepository,
    IPasswordEncoderPort passwordEncoder,
    ITimeProviderPort timeProvider,
    IRandomGeneratorPort randomGenerator,
    IEventPublisherPort eventPublisher)
{
    public async Task<User> Handle(SignupCommand command, CancellationToken cancellationToken)
    {
        var newUser = new NewUser
        {
            Id = randomGenerator.NewGuid(),               // not Guid.NewGuid() — a port
            Username = new Username(command.Username),     // value object validates the format
            PasswordHash = passwordEncoder.Hash(command.Password),
            CreatedAt = timeProvider.UtcNow,               // not DateTimeOffset.UtcNow — a port
        };

        var user = await userRepository.Create(newUser, cancellationToken);
        await eventPublisher.Publish(new UserSignedUpEvent { UserId = user.Id }, cancellationToken);
        return user;
    }
}
```

Unit-test it exhaustively with fakes for every port (deterministic clock/GUID make assertions exact). The repository port grows one method:

```csharp
public interface IUserRepositoryPort
{
    Task<ImmutableList<User>> FindAll(CancellationToken cancellationToken);
    Task<User> Create(NewUser newUser, CancellationToken cancellationToken);   // new
}
```

Add `Create` to both the fake and the real adapter — the contract test from §8.1 grows a `Create_persists_and_returns_the_user` case, and both implementations must pass it.

**Step 3 — translate the DB constraint in the adapter.** The unique index is the source of truth for "username taken"; the adapter turns the low-level DB error into the domain exception (`23505` is PostgreSQL's unique-violation code):

```csharp
public async Task<User> Create(NewUser newUser, CancellationToken cancellationToken)
{
    var entity = UserDbEntity.FromNewUser(newUser);
    dbContext.Users.Add(entity);
    try
    {
        await dbContext.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
    {
        throw new UsernameAlreadyExistsException(newUser.Username);
    }
    return entity.ToDomain();
}
```

(Grow `UserDbEntity` with `PasswordHash`/`CreatedAt` columns + a `FromNewUser` factory, and add a migration for them — `dotnet ef migrations add AddUserAuthColumns`.)

**Step 4 — the endpoint trio** (`Features/Users/Api/Endpoints/Signup/`). A POST *does* have a body, so it has a request record — validated by DataAnnotations (§5.3's `AddValidation()` enforces it before the handler runs):

```csharp
// SignupEndpoint.cs
public sealed class SignupEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/auth/public/signup", Handle).AllowAnonymous();   // /public/ ⇒ anonymous

    private static async Task<Created<SignupEndpointResponse>> Handle(
        SignupEndpointRequest request, SignupUseCase useCase, CancellationToken cancellationToken)
    {
        var user = await useCase.Handle(request.ToCommand(), cancellationToken);
        return TypedResults.Created($"/users/{user.Id}", SignupEndpointResponse.From(user));
    }
}

// SignupEndpointRequest.cs
public sealed record SignupEndpointRequest
{
    [Required, MinLength(1), MaxLength(50)] public required string Username { get; init; }
    [Required, MinLength(8)] public required string Password { get; init; }

    public SignupCommand ToCommand() => new() { Username = Username, Password = Password };
}

// SignupEndpointResponse.cs
public sealed record SignupEndpointResponse
{
    public required Guid Id { get; init; }
    public required string Username { get; init; }
    public static SignupEndpointResponse From(User user) =>
        new() { Id = user.Id, Username = user.Username.Value };
}
```

**Step 5 — map the exception to a clean 409.** A feature exception handler, registered *before* the terminal `CatchAllExceptionHandler` (§5.2); handlers are mapping tables:

```csharp
internal sealed class UsersExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken cancellationToken)
    {
        if (ex is not UsernameAlreadyExistsException) return false;   // not mine — let the next handler try

        ctx.Response.StatusCode = StatusCodes.Status409Conflict;
        return await problemDetails.TryWriteAsync(new()
        {
            HttpContext = ctx,
            ProblemDetails = { Title = "errors.username_already_exists", Status = 409 },
        });
    }
}
```

Assert the **exact** ProblemDetails JSON in an integration test (the payload is the contract). `UserSignedUpEvent` (a domain event, `Domain/Events/`) publishes through `IEventPublisherPort` for downstream features (audit) to consume — features stay isolated, talking only via events.

**Verify:** `curl -X POST /api/auth/public/signup -d '{"username":"grace","password":"secret123"}'` → `201` with the new user; repeat the same username → `409` with `title: "errors.username_already_exists"`. **Tinker:** replace the injected clock fake with a fixed instant in the unit test → `CreatedAt` is exactly that instant, proving the domain read no real clock.

### 8.3 Onward

Authentication (JWT issuing, cookies, refresh rotation — [doc 09 §4–5](09-security-observability-and-error-handling.md#4-jwt-and-cookie-transport)), the audit pipeline, scheduled session cleanup — each is "Phase 8 again" on a new slice: **domain → contract → adapter → endpoint → gates updated the same day**. You now have every move; the rest is repetition on new nouns. Keep the [Feature Matrix](11-feature-matrix.md) honest as you go.

## Navigation

⬅️ Previous: [Feature Matrix](11-feature-matrix.md) · ➡️ Next: [Guide for Java/Spring Developers](13-guide-for-java-spring-developers.md)
