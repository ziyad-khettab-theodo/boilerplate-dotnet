    # Hands-On Build Guide

This guide constructs the entire platform **by hand**: every file, what each line does, how to verify it works, and how to break it on purpose to watch the gate fire. Each step cites the normative doc it implements — this guide is the *how*, those docs are the *law*.

The order is deliberately **code first, gates second**: you build a small but real end-to-end slice, test it, and only then layer the quality gates on top — so every gate has existing code to bite on, and you can tinker (violate a rule, watch the failure, fix it) instead of taking the gate on faith.

How to use it:

- Work phase by phase; **do not skip verify blocks** — they are the point.
- Type the files rather than pasting them; the friction is where the learning happens.
- When a tool's exact flag or option differs from what's written here (packages evolve), trust the tool's `--help` and current README, make it work, then update this guide in the same commit — the guide must never drift from reality.
- After each phase, update the [Feature Matrix](11-feature-matrix.md) Status column and commit.

Phases: 1 Project essentials → 2 First end-to-end endpoint → 3 Testing it → 4 Quality gates → 5 Platform hardening → 6 Local stack → 7 CI/CD → 8 Persistence and features.

---

## Phase 1 — Project Essentials

> **Concept primer.** A **`.csproj`** is a project: one buildable unit producing one assembly (a `.dll`), declaring its package and project references in a few lines of XML. A **solution** (`.slnx`) is just the list of projects that belong together. The **SDK** (`dotnet`) builds, runs, and tests everything from the CLI.

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
  "sdk": { "version": "10.0.100", "rollForward": "latestFeature" },
  "test": { "runner": "Microsoft.Testing.Platform" }
}
```

(Set `version` to your `dotnet --version` major; `rollForward` accepts newer feature bands so teammates aren't blocked by patch drift.)

### 1.3 Solution and the three projects

```bash
cd api
dotnet new sln --name DotnetBoilerplate          # creates DotnetBoilerplate.slnx
dotnet new classlib -n Theodo.DotnetBoilerplate.Domain -o src/Domain
dotnet new classlib -n Theodo.DotnetBoilerplate.Infrastructure -o src/Infrastructure
dotnet new web      -n Theodo.DotnetBoilerplate.Api -o src/Api
dotnet sln add src/Domain src/Infrastructure src/Api
dotnet add src/Infrastructure reference src/Domain
dotnet add src/Api reference src/Domain src/Infrastructure
rm src/*/Class1.cs
```

The reference commands *are* the architecture ([Architecture Overview §3.1](02-architecture-overview.md#31-compile-time-tier-project-references)): `Api → Domain + Infrastructure`, `Infrastructure → Domain`, `Domain → nothing`. Note what's absent — no command ever gives `Domain` a reference.

**Verify:** `dotnet build` succeeds; `dotnet run --project src/Api --urls http://localhost:8080` serves the template's hello-world.

**Break it on purpose:** try `dotnet add src/Domain reference src/Infrastructure` → the CLI refuses: *circular reference*. The dependency direction is physically one-way.

---

## Phase 2 — First End-to-End Endpoint

Goal: `GET /api/users` flowing **endpoint → use case → port → adapter**, with the folder taxonomy from [doc 03](03-project-structure-and-conventions.md) built as you go. No database yet — the first adapter is in-memory, which is exactly the hexagonal point: the domain won't know the difference when PostgreSQL replaces it in phase 8.

### 2.1 Domain: the hexagon's first cells

`Domain` gets its **only** allowed package (immutable collections are part of the domain allowlist):

```bash
dotnet add src/Domain package System.Collections.Immutable
```

`src/Domain/Common/ValueObjects/Username.cs` — a value object: validated at construction, impossible to hold an invalid value ([doc 03 §2.4](03-project-structure-and-conventions.md#24-entities-vs-value-objects)):

```csharp
namespace Theodo.DotnetBoilerplate.Domain.Common.ValueObjects;

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

`src/Domain/Features/Users/Entities/User.cs` — an entity: identity + data, as an immutable record:

```csharp
namespace Theodo.DotnetBoilerplate.Domain.Features.Users.Entities;

public sealed record User
{
    public required Guid Id { get; init; }
    public required Username Username { get; init; }
}
```

`src/Domain/Features/Users/Ports/IUserRepositoryPort.cs` — the domain's demand on the outside world:

```csharp
namespace Theodo.DotnetBoilerplate.Domain.Features.Users.Ports;

public interface IUserRepositoryPort
{
    Task<ImmutableList<User>> FindAll(CancellationToken cancellationToken);
}
```

`src/Domain/Features/Users/UseCases/GetUsers/GetUsersQuery.cs` and `GetUsersUseCase.cs` — one operation, one class, one `Handle` ([doc 02 §5](02-architecture-overview.md#5-use-case-contract)):

```csharp
public sealed record GetUsersQuery;

public class GetUsersUseCase(IUserRepositoryPort userRepository)
{
    public Task<ImmutableList<User>> Handle(GetUsersQuery query, CancellationToken cancellationToken) =>
        userRepository.FindAll(cancellationToken);
}
```

(Thin today — pagination, filtering, and authorization context arrive later and will live *here*, not in the endpoint.)

### 2.2 Infrastructure: the first adapter

`src/Infrastructure/Adapters/InMemoryUserRepository.cs`:

```csharp
namespace Theodo.DotnetBoilerplate.Infrastructure.Adapters;

public class InMemoryUserRepository : IUserRepositoryPort
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

`src/Api/Common/Endpoints/IEndpoint.cs` — the one-interface convention behind REPR ([doc 02 §6](02-architecture-overview.md#6-endpoint-contract-repr)):

```csharp
namespace Theodo.DotnetBoilerplate.Api.Common.Endpoints;

public interface IEndpoint
{
    static abstract void Map(IEndpointRouteBuilder app);
}
```

`src/Api/Common/Endpoints/EndpointDiscovery.cs` — find every `IEndpoint` in this assembly and call its `Map` once at startup:

```csharp
public static class EndpointDiscovery
{
    public static void MapEndpoints(this IEndpointRouteBuilder app)
    {
        var endpointTypes = typeof(IEndpoint).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsAssignableTo(typeof(IEndpoint)));
        foreach (var type in endpointTypes)
            type.GetMethod(nameof(IEndpoint.Map))!.Invoke(null, [app]);
    }
}
```

`src/Api/Common/ServiceRegistration/UseCaseRegistration.cs` — use cases register by naming convention so the domain never carries DI attributes ([doc 02 §8](02-architecture-overview.md#8-dependency-injection-wiring-strategy)):

```csharp
public static class UseCaseRegistration
{
    public static IServiceCollection AddUseCases(this IServiceCollection services)
    {
        var useCases = typeof(Domain.Common.ValueObjects.Username).Assembly.GetTypes()
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

`src/Api/Features/Users/Endpoints/GetUsers/GetUsersEndpoint.cs` + `GetUsersEndpointResponse.cs` — a GET has no body, so this endpoint has no request record; the response record is still endpoint-local and mapped explicitly:

```csharp
public class GetUsersEndpoint : IEndpoint
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

`src/Api/Program.cs` — deliberately minimal for now; each later phase adds one block:

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

> ⚠️ **This endpoint is temporarily unauthenticated** — there is no security pipeline yet. Phase 5 turns on deny-by-default authorization, this exact endpoint will start returning 401, and you'll make its authorization explicit. That breakage is scheduled on purpose.

**Verify:**

```bash
dotnet run --project src/Api --urls http://localhost:8080
curl http://localhost:8080/api/users     # → 200, JSON array with ada and linus
```

**Tinker:** trace the request through the four files in call order ([doc 01 §3](01-onboarding-guide.md#3-first-architecture-trace)). Then try to make `GetUsersUseCase` return usernames uppercased *from the endpoint* — notice how wrong it feels mechanically: the endpoint has no business seeing that rule. Put it in the use case, observe the endpoint didn't change. That's [doc 02 §4](02-architecture-overview.md#4-what-belongs-in-the-domain-vs-in-adapters) in your hands.

---

## Phase 3 — Testing the Slice

> **Concept primer.** Test projects are ordinary projects referencing a test framework. Ours split by **boot cost** ([doc 05 §2](05-testing-platform.md#2-test-categories-and-naming)): unit tests construct plain objects; integration tests boot an in-memory HTTP host (`WebApplicationFactory`) and exercise the real pipeline.

### 3.1 Projects

```bash
dotnet new install xunit.v3.templates    # once per machine
dotnet new xunit3 -n Theodo.DotnetBoilerplate.Domain.UnitTests -o tests/Domain.UnitTests
dotnet new xunit3 -n Theodo.DotnetBoilerplate.Api.IntegrationTests -o tests/Api.IntegrationTests
dotnet new classlib -n Theodo.DotnetBoilerplate.TestHelpers -o tests/TestHelpers
dotnet sln add tests/Domain.UnitTests tests/Api.IntegrationTests tests/TestHelpers

dotnet add tests/TestHelpers reference src/Domain
dotnet add tests/Domain.UnitTests reference src/Domain tests/TestHelpers
dotnet add tests/Api.IntegrationTests reference src/Api tests/TestHelpers

dotnet add tests/Domain.UnitTests package AwesomeAssertions
dotnet add tests/Api.IntegrationTests package AwesomeAssertions
dotnet add tests/Api.IntegrationTests package Microsoft.AspNetCore.Mvc.Testing
```

Read the reference lines again: `Domain.UnitTests` **cannot see** Infrastructure or Api. "Unit tests are only for domain code" ([doc 05 §3](05-testing-platform.md#3-unit-test-scope)) is compile-time physics here, not a convention.

### 3.2 The first fake and unit test

`tests/TestHelpers/Fakes/FakeUserRepository.cs` — a working in-memory implementation of the port, with test-only inspection helpers on the fake (never on the port — [doc 05 §4](05-testing-platform.md#4-fakes-over-mocks)):

```csharp
public class FakeUserRepository : IUserRepositoryPort
{
    private readonly List<User> users = [];

    public FakeUserRepository Containing(params User[] seed) { users.AddRange(seed); return this; }

    public Task<ImmutableList<User>> FindAll(CancellationToken cancellationToken) =>
        Task.FromResult(users.ToImmutableList());
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

`tests/Domain.UnitTests/Features/Users/UseCases/GetUsers/GetUsersUseCaseUnitTests.cs` — note the folder mirrors the production path, the suffix carries the category, and the single `// Act` marker ([doc 05 §9](05-testing-platform.md#9-the-act-pattern)):

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

`tests/Api.IntegrationTests/Features/Users/GetUsersIntegrationTests.cs` — boots the real pipeline in memory:

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

**Verify:** `dotnet test` → 2 tests green. (A `BaseWebIntegrationTests` base class extracts the factory/client plumbing as soon as a second integration test wants it — that's how the [doc 05 §7](05-testing-platform.md#7-test-infrastructure-building-blocks) building blocks are born: extracted from repetition, never speculative.)

**Tinker:** break the use case (return an empty list) → the unit test fails with a readable message *and* the integration test fails. Fix it; now break only the JSON mapping (rename a response property) → the unit test stays green, only the integration test catches it. Each test layer guards its own boundary.

---

## Phase 4 — Quality Gates (retrofit onto real code)

Now the payoff of code-first: every gate below immediately confronts the code you just wrote. Expect findings — fixing them is the exercise.

### 4.1 Repo-wide build policy

> **Concept primer.** **`Directory.Build.props`** is MSBuild's inheritance mechanism: properties in it apply to *every* project beneath it. Policy lives there so no project can quietly opt out.

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

Then **delete** the now-duplicated properties from every `.csproj` (each shrinks to its SDK line + references + packages) and rebuild. The analyzers will almost certainly flag things in your phase-2/3 code — missing `sealed`, unused usings, naming. **Fix every finding**; this is the "existing code must follow the rules" moment you built this order for. *(Implements [toolchain §2](06-build-toolchain-and-quality-gates.md#2-zero-warning-compilation-and-analyzers).)*

### 4.2 Central package versions + lock files

Create `api/Directory.Packages.props` (`ManagePackageVersionsCentrally=true`), then migrate: move each `Version="…"` out of the csprojs into `<PackageVersion>` items. `dotnet restore` writes `packages.lock.json` per project — commit them.

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
dotnet add src/Domain package SonarAnalyzer.CSharp
dotnet add src/Infrastructure package SonarAnalyzer.CSharp
dotnet add src/Api package SonarAnalyzer.CSharp
dotnet add src/Domain package Microsoft.CodeAnalysis.BannedApiAnalyzers
```

(Analyzer packages are build-time only — mark them `PrivateAssets="all"`.) Fix whatever Sonar finds in existing code. Then ban ambient state in the domain — `api/src/Domain/BannedSymbols.txt`:

```text
P:System.DateTime.Now;Domain is deterministic - inject ITimeProviderPort
P:System.DateTime.UtcNow;Domain is deterministic - inject ITimeProviderPort
P:System.DateTime.Today;Domain is deterministic - inject ITimeProviderPort
P:System.DateTimeOffset.Now;Domain is deterministic - inject ITimeProviderPort
P:System.DateTimeOffset.UtcNow;Domain is deterministic - inject ITimeProviderPort
M:System.Guid.NewGuid;Domain is deterministic - inject IRandomGeneratorPort
M:System.Random.#ctor;Domain is deterministic - inject IRandomGeneratorPort
```

wired in the Domain csproj: `<AdditionalFiles Include="BannedSymbols.txt" />`.

**Tinker:** write `var now = DateTime.Now;` in `GetUsersUseCase` → build error **RS0030** with your own message. The same line in `InMemoryUserRepository` **compiles** — adapters are exactly where clocks belong. *(Implements [toolchain §2.1](06-build-toolchain-and-quality-gates.md#21-analyzer-packs).)*

### 4.5 Architecture tests

```bash
dotnet new xunit3 -n Theodo.DotnetBoilerplate.ArchitectureTests -o tests/ArchitectureTests
dotnet sln add tests/ArchitectureTests
dotnet add tests/ArchitectureTests reference src/Domain src/Infrastructure src/Api
dotnet add tests/ArchitectureTests package TngTech.ArchUnitNET.xUnitV3
```

Give each source project an empty `AssemblyMarker.cs` (`public sealed class AssemblyMarker;`) so rules can name assemblies without magic strings, then write the first rules — `tests/ArchitectureTests/HexagonalArchitectureRulesUnitTests.cs`:

```csharp
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

public class HexagonalArchitectureRulesUnitTests
{
    private static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies(
            typeof(Domain.AssemblyMarker).Assembly,
            typeof(Infrastructure.AssemblyMarker).Assembly,
            typeof(Api.AssemblyMarker).Assembly)
        .Build();

    [Fact]
    public void Use_cases_have_exactly_one_public_Handle_method() =>
        Classes().That().HaveNameEndingWith("UseCase")
            .Should().FollowCustomCondition(c =>
                c.GetMethodMembers().Count(m => m.Visibility == Visibility.Public && !m.IsConstructor()) == 1
                && c.GetMethodMembers().Any(m => m.Name.StartsWith("Handle(")),
                "have exactly one public method, named Handle",
                "use cases are one operation each")
            .Check(Architecture);

    [Fact]
    public void Endpoint_classes_live_in_an_endpoints_folder_matching_their_name() =>
        Classes().That().ImplementInterface(typeof(Api.Common.Endpoints.IEndpoint))
            .Should().HaveNameEndingWith("Endpoint")
            .AndShould().ResideInNamespace(@".*\.Endpoints\..*", true)
            .Check(Architecture);
}
```

(The fluent API surface evolves — keep the *rule names* and *reasons* aligned with the [inventory](06-build-toolchain-and-quality-gates.md#6-architecture-test-inventory) and adapt the query syntax to the current ArchUnitNET README.) Grow the suite one rule at a time: **every convention you adopt from here on gets its rule the same day.**

**Tinker:** move `GetUsersEndpoint.cs` into `Features/Users/` directly (out of `Endpoints/GetUsers/`) → the placement rule fails, naming the violation. Move it back. Rename `Handle` to `Execute` → the use-case rule fails. This is [doc 02 §3.2](02-architecture-overview.md#32-architecture-test-tier-fine-grained-rules) biting.

### 4.6 Coverage + mutation gates, and `./validate`

`validate` (repo root, `chmod +x`) — the single pre-push command, now assembling every gate in triage order ([toolchain §5](06-build-toolchain-and-quality-gates.md#5-command-intent)):

```bash
#!/bin/sh
set -eu
cd "$(dirname "$0")/api"

echo "── format"       && dotnet csharpier check .
echo "── build"        && dotnet build --configuration Release
echo "── architecture" && dotnet test tests/ArchitectureTests --no-build --configuration Release
echo "── unit"         && dotnet test tests/Domain.UnitTests --no-build --configuration Release \
                            -- --coverage --coverage-output-format cobertura
echo "── integration"  && dotnet test tests/Api.IntegrationTests --no-build --configuration Release \
                            -- --coverage --coverage-output-format cobertura

echo "── coverage gate"
if grep -q "Theodo.DotnetBoilerplate" DotnetBoilerplate.slnx; then LINE=100; BRANCH=100; MUT=100; else LINE=95; BRANCH=80; MUT=95; fi
dotnet reportgenerator \
  -reports:"tests/**/TestResults/**/*.cobertura.xml" -targetdir:artifacts/coverage \
  "-reporttypes:Html;TextSummary" \
  "minimumCoverageThresholds:lineCoverage=$LINE;branchCoverage=$BRANCH"

echo "── mutation"     && dotnet stryker --test-runner mtp --break-at $MUT
echo "OK"
```

Notes: the coverage flags belong to the testing-platform collector (`dotnet test -- --help` lists them); **ReportGenerator is the gate** — it exits non-zero below threshold; the `grep` implements the template-100%/client-baseline switch from [toolchain §3](06-build-toolchain-and-quality-gates.md#3-coverage-and-mutation-thresholds). Add `api/stryker-config.json` pointing at the Domain project with `Domain.UnitTests` as test project, thresholds `{ high: 95, low: 90, break: 95 }`, and keep `--test-runner mtp` — the default runner mis-scores this stack.

**Tinker (the best one):** in `GetUsersUseCaseUnitTests`, delete the assertions but keep the call. Coverage stays **100%** — the line was executed! — but Stryker now reports surviving mutants: it mutated the use case and no test noticed. Restore the assertions, watch the mutants die. You now understand *why* both gates exist ([toolchain §3 primer](06-build-toolchain-and-quality-gates.md#3-coverage-and-mutation-thresholds)).

---

## Phase 5 — Platform Hardening

Each block below hardens the running app; add them to `Program.cs` one commit at a time, watching what each breaks.

### 5.1 Deny-by-default security

```csharp
builder.Services.AddAuthentication().AddJwtBearer();   // token read from cookie: configured with the auth feature
builder.Services.AddAuthorization(o =>
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
// pipeline: app.UseAuthentication(); app.UseAuthorization(); before MapEndpoints()
```

**Your phase-2 endpoint now returns 401.** That's the scheduled breakage: nothing declared its exposure, so the fallback secured it ([doc 02 §6.1](02-architecture-overview.md#61-authorization-rule)). Resolve it *explicitly*: `.RequireAuthorization()` on `/users` — and since no login exists yet, integration tests authenticate via a test authentication scheme registered in the test factory (`AuthenticationBuilder.AddScheme` with a handler that issues a test principal), while `curl` correctly gets 401 until the authentication feature lands in phase 8. Add the endpoint-authorization architecture rule now (every endpoint declares intent; `/public/` ⇔ `AllowAnonymous`).

### 5.2 Error contract

`AddProblemDetails()` + the ordered `IExceptionHandler` chain ending in a `CatchAllExceptionHandler` (500, `errors.internal`, full exception logged, nothing internal in the body), plus the `ErrorCode` interface in `Common/ExceptionHandling/`. **Tinker:** throw from the use case → the response is a clean ProblemDetails, the console has the stack trace. *(Implements [doc 09 §7](09-security-observability-and-error-handling.md#7-error-contract).)*

### 5.3 Strict JSON + validated options

```csharp
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
builder.Services.AddValidation();   // .NET 10 built-in DataAnnotations validation for minimal APIs
```

Options records (`Common/Properties/`) bind with `.BindConfiguration(...).ValidateDataAnnotations().ValidateOnStart()` — a missing setting crashes at boot naming the option, never at 3 a.m. downstream. **Tinker:** POST an unknown JSON field to any future endpoint → 400; delete a required config value → the app refuses to start. *(Implements [doc 09 §8.3](09-security-observability-and-error-handling.md#83-openapi-and-serialization).)*

### 5.4 Health, OpenAPI, telemetry

- `AddHealthChecks()` + `MapHealthChecks("/public/health").AllowAnonymous()` — the path convention's first legitimate use.
- `AddOpenApi()` + Scalar UI mapped **only when** `OpenApi:Enabled` (Development default) — confirm the package's current UI route and pin it in config.
- OpenTelemetry: resource identity from `OTEL_SERVICE_NAME`/`APP_ENVIRONMENT`, ASP.NET Core + HTTP instrumentation, JSON console logs outside Development, `UseOtlpExporter()` when an endpoint is configured, `X-Trace-Id` response header middleware. *(Implements [doc 09 §8.4–8.6](09-security-observability-and-error-handling.md#84-structured-logging-and-trace-correlation).)*

**Verify (phase gate):** health = 200 anonymously · `/api/users` = 401 raw, 200 via test auth in integration tests · unknown JSON field = 400 · docs UI in Development only.

---

## Phase 6 — Local Stack

`.env.template` (repo root: `POSTGRES_USER/PASSWORD/DB`, `COMPOSE_PROFILES=localdev`, `API_PORT=8080`) rendered by `./init`. `docker-compose.dependencies.yml`: `db` (PostgreSQL 18, **digest-pinned**, named volume), `pgadmin` (profile `localdev`), `otel-lgtm` (Grafana all-in-one — UI `:3333`, OTLP `:4317/:4318`). `docker-compose.yml`: the `api` service from `api/Dockerfile` (multi-stage publish, non-root user, `HEALTHCHECK` probing `/api/public/health`, bound to `127.0.0.1`), including the dependencies file. Point Development OTLP at the local stack.

**Verify:** `docker compose up -d` → all healthy; run the API, hit `/api/users`, then find that exact request's trace in Grafana and its JSON log line joined by `trace_id`. *(Implements [doc 09 §8.7](09-security-observability-and-error-handling.md#87-local-observability-stack); pinning per [doc 10 §5](10-ci-cd-and-governance.md#5-supply-chain-pinning).)*

---

## Phase 7 — CI/CD

Three commits, mirroring [doc 10](10-ci-cd-and-governance.md):

1. **Minimal `ci.yml`:** checkout (SHA-pinned actions), `setup-dotnet` from `global.json`, then exactly the `./validate` stages as jobs (`compile` → `tests` → `mutation`) with artifact reuse. Local and CI check the *same things* — every future gate lands in both in one commit.
2. **Topology:** `all.yml` → `all_configure.yml` (change detection: docs-only PRs skip backend jobs) → `all_ci.yml` fan-out (docs-checker: markdownlint + link check over `docs/`; OpenGrep SAST; backend) → the aggregate **"Workflow end marker"** job.
3. **Governance:** `default_branch_ruleset.json` (1 approval, linear history, squash/rebase, required check = the marker), Renovate (`.ci/renovate/`, 5-day minimum release age, grouped non-major, patch automerge), scheduled daily dependency audit.

**Verify:** a PR with a formatting violation → only the format job red, marker red, merge blocked. A docs-only PR → backend jobs skipped, marker green.

---

## Phase 8 — Persistence and Real Features

### 8.1 PostgreSQL behind the same port

The hexagonal payoff: replace the in-memory adapter **without touching the domain or the endpoint**.

1. Extract the behavioral spec first: abstract `UserRepositoryPortContractTests` in `TestHelpers`; run it against `FakeUserRepository` ([doc 05 §5](05-testing-platform.md#5-port-contract-tests)).
2. `Infrastructure`: EF Core + Npgsql packages, `UserDbEntity` + `IEntityTypeConfiguration` + `AppDbContext`, first migration (`dotnet ef migrations add`  — **read the generated file**), and the real `UserRepository` adapter mapping entity ↔ domain both ways ([doc 08 §1–2](08-data-persistence-and-migrations.md#1-persistence-boundaries)).
3. Run the same contract tests against the real adapter via Testcontainers (`Testcontainers.XunitV3`, image tag read from the compose file); add `[AssertQueryCount]`/`[ExpectedQueries]` via an EF command interceptor; add the schema-drift tests (`Database.HasPendingModelChanges()`); add the destructive-migration CI guard.
4. Swap the registration in `AddAdapters()`; retire `InMemoryUserRepository` (its spirit lives on as `FakeUserRepository`). `curl /api/users` — same response, new engine.

### 8.2 First write path: signup

Domain-first, letting each gate teach: `NewUser` value object, `UsernameAlreadyExistsException`, `SignupCommand` + `SignupUseCase` (ports: repository, password encoder, time, random, event publisher — the banned-APIs rule now *forces* the port design) with exhaustive unit tests; unique-constraint translation in the adapter; `Endpoints/Signup/` trio on `/auth/public/signup` (`.AllowAnonymous()` — path convention); `UsersExceptionHandler` mapping to 409/`errors.username_already_exists` + handler tests; `UserSignedUpEvent` through `IEventPublisherPort`.

### 8.3 Onward

Authentication (JWT issuing, cookies, refresh rotation — [doc 09 §4–5](09-security-observability-and-error-handling.md#4-jwt-and-cookie-transport)), the audit pipeline, scheduled session cleanup — each is "phase 8 again" on a new slice: domain → contract → adapter → endpoint → gates updated the same day. Keep the [Feature Matrix](11-feature-matrix.md) honest as you go.

## Navigation

⬅️ Previous: [Feature Matrix](11-feature-matrix.md) · ➡️ Next: [Guide for Java/Spring Developers](13-guide-for-java-spring-developers.md)
