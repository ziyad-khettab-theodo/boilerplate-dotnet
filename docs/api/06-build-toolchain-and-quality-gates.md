# Build Toolchain and Quality Gates

Quality gates enforce the architecture and coding contracts automatically. Every gate described here **fails the build or blocks the merge** — none are advisory. A red build always means a real problem; that guarantee is what makes the codebase safe to change quickly.

## 1. Baseline and Toolchain Pinning

- **.NET 10 (LTS)**, C# latest. The exact SDK version is pinned in `api/global.json` — every machine and CI runner builds with the same toolchain.
- `global.json` also selects the modern test runner (`"test": { "runner": "Microsoft.Testing.Platform" }`), so `dotnet test` behaves identically everywhere.
- CLI tools are **repo-local and version-pinned** in `api/.config/dotnet-tools.json` (`csharpier`, `dotnet-ef`, `dotnet-stryker`, `dotnet-reportgenerator-globaltool`); `dotnet tool restore` installs them. No global installs, no version drift between machines.
- Shared build policy lives in `api/Directory.Build.props` and applies to **every** project automatically — individual projects cannot silently opt out.

**Why:** reproducibility starts with an identical toolchain; central policy files make the gates un-forkable per project.

## 2. Zero-Warning Compilation and Analyzers

`Directory.Build.props` sets, for all projects:

| Property | Value | Effect |
|---|---|---|
| `Nullable` | `enable` | nullable reference types everywhere (see [Nullability](07-nullability-and-nullable-reference-types.md)) |
| `TreatWarningsAsErrors` | `true` | **any** warning fails the build — compiler, nullability, analyzers |
| `AnalysisLevel` | `latest-all` | the full built-in .NET analyzer set at its newest level |
| `EnforceCodeStyleInBuild` | `true` | `.editorconfig` code-style rules are build diagnostics, not IDE hints |
| `GenerateDocumentationFile` | `true` (Api) | XML comments feed the OpenAPI document |

### 2.1 Analyzer Packs

| Pack | Scope | Guardrails it adds |
|---|---|---|
| Built-in .NET analyzers (`latest-all`) | all projects | correctness, performance, API-usage, security basics |
| `SonarAnalyzer.CSharp` | all projects | bug patterns, code smells, cognitive-complexity limits |
| `Microsoft.CodeAnalysis.BannedApiAnalyzers` | **Domain only** | bans ambient state — `DateTime.Now/UtcNow/Today`, `DateTimeOffset.Now/UtcNow`, `Guid.NewGuid`, `new Random()` (`BannedSymbols.txt`); domain code must use `ITimeProviderPort` / `IRandomGeneratorPort` |
| Custom analyzers (roadmap) | tests | Act-pattern enforcement (see [Testing Platform §9](05-testing-platform.md#9-the-act-pattern)) |

Severity tuning happens **only** in `.editorconfig`, in review, with a comment explaining why — never inline `#pragma` suppressions without a linked rationale.

**Why banned APIs per project:** the adapters implementing `TimeProvider` and `RandomGenerator` legitimately call the banned members; scoping the ban to `Domain` encodes "the domain is deterministic" exactly, with a compile error (`RS0030`) as enforcement.

### 2.2 Formatting: CSharpier Owns Layout

- **CSharpier** (opinionated, zero-config) is the single source of truth for code layout: `dotnet csharpier format .` fixes, `dotnet csharpier check .` gates CI.
- The pre-commit hook formats fully-staged files automatically and **fails the commit** for partially-staged files that are not compliant (stage or unstage whole files and retry).
- The `.editorconfig` formatting rule `IDE0055` is disabled so the analyzers never fight the formatter; analyzers own *style and correctness*, CSharpier owns *whitespace and layout*.

**Why:** formatting debates cost review time and pollute diffs; an auto-applied formatter ends them. Splitting layout (formatter) from style (analyzers) gives each tool a single job.

## 3. Coverage and Mutation Thresholds

> **Concept primer.** Line/branch coverage proves code was *executed* by tests; **mutation testing** proves tests *notice* when behavior changes: the tool injects small bugs ("mutants") and checks that some test fails. Surviving mutants are behavior your tests silently allow to break. *Test strength* is the kill rate among mutants that tests actually reached.

Baseline thresholds (client projects):

| Metric | Scope | Threshold |
|---|---|---|
| Line coverage | total | **≥ 95%** |
| Branch coverage | total | **≥ 80%** |
| Line coverage | Domain | **≥ 95%** |
| Mutation score | Domain + Utils | **≥ 95%** |
| Test strength | Domain + Utils | **≥ 98%** |

**Template profile:** while the projects still carry the `Theodo.DotnetBoilerplate` name prefix, an MSBuild condition raises every threshold to **100%**. Renaming the projects for a client (bootstrap step 1) removes the marker automatically and the baseline above applies — unless the project sets stricter values.

Mechanics:

- Coverage is collected per test project by the test-platform coverage extension (cobertura format) and gated by **ReportGenerator**, whose `minimumCoverageThresholds` setting exits non-zero below the bar. One Domain-scoped invocation enforces the Domain threshold; one merged invocation enforces the totals and produces the HTML report.
- Mutation testing runs **Stryker.NET** against the `Domain` project with its unit-test project, using the test-platform runner (`--test-runner mtp`); `thresholds.break` in `stryker-config.json` fails the run below the bar.

**Why these numbers:** the domain is the product — near-total coverage *and* mutation strength there is the point of hexagonal isolation. Thresholds may only move up; lowering one is an architecture discussion with your tech lead, not a config edit.

## 4. Dependency Integrity and Vulnerability Gates

- **Central Package Management:** every package version is declared once in `api/Directory.Packages.props`; projects reference packages without versions. No version divergence is possible.
- **Lock files:** `packages.lock.json` per project, committed; CI restores with `--locked-mode`, so an unreviewed dependency-graph change fails the build.
- **Vulnerability audit:** NuGet audits the full dependency graph (direct + transitive) on every restore against the public advisory database; high and critical findings (`NU1903`, `NU1904`) are **errors**. Per-advisory suppression requires an explicit entry with a written reason.
- **Automated updates:** Renovate opens grouped update PRs on a schedule with a minimum release age of 5 days (supply-chain caution); patch updates auto-merge, everything else needs review. A scheduled daily audit catches advisories published between updates.

**Why:** the lock file makes the dependency graph a reviewed artifact; the audit gate turns CVE response from a periodic chore into an immediate red build.

## 5. Command Intent

| Command | Intent |
|---|---|
| `./init` | bootstrap after clone: render `.env`, `dotnet tool restore`, install git hooks — repo-local, idempotent |
| `./validate` | **the default full local validation** before every push: format → build (all analyzers) → architecture tests → unit tests → integration tests → coverage gate → mutation gate |
| `dotnet csharpier format .` | fix formatting |
| `dotnet build` | compile + analyzers + nullability (fastest signal) |
| `dotnet test` | full test suite (project order = arch → unit → integration) |
| `dotnet stryker --test-runner mtp` | mutation testing in isolation (debugging surviving mutants) |
| `docker compose up -d` | local dependencies (PostgreSQL, observability stack) |

### 5.1 Failure Triage Order

When `./validate` or CI fails, investigate in this order — earlier layers explain later ones:

1. **Architecture rule failures** (`*RulesUnitTests`) — structure is wrong; nothing downstream is trustworthy.
2. **Analyzer / formatting errors** — read the diagnostic ID; the fix is usually mechanical.
3. **Nullability errors** — see the [patterns catalogue](07-nullability-and-nullable-reference-types.md#4-common-error-patterns-and-fixes).
4. **Integration/application test failures** — boundary behavior.
5. **Coverage/mutation failures** — add missing *behavior* tests (never assertion-free tests to inflate numbers).

Reports land under `api/artifacts/` (`test-results/`, `coverage/`, `mutation/`) and `.reports/opengrep/`.

## 6. Architecture Test Inventory

All in `tests/ArchitectureTests`, executed before everything else.

| Rule class | Protects |
|---|---|
| `HexagonalArchitectureRulesUnitTests` | domain dependency allowlist; feature-slice isolation; fields typed as ports, not adapters; audit-feature exception for integration events |
| `DesignRulesUnitTests` | no service-locator resolution in business code; no framework attributes on domain types; immutable collection types in domain signatures |
| `CodingRulesUnitTests` | no generic `Exception` throw/catch in domain; logger conventions; options types are validated |
| `NamingConventionRulesUnitTests` (root) | `*Event` ⇔ `IEvent`; exception naming ⇔ inheritance ⇔ placement |
| `Domain/NamingConventionRulesUnitTests` | ports `I*Port` in `Ports/`; use cases under `UseCases/<name>/`; only `*UseCase|*Command|*Query` there |
| `Domain/UseCaseRulesUnitTests` | one public `Handle`, ≤ 1 input parameter |
| `Api/EndpointConventionRulesUnitTests` | endpoint naming/placement; every endpoint declares authorization; `/public/` ⇔ `AllowAnonymous` |
| `Api/RequestResponseConventionRulesUnitTests` | request/response record locality, allowed field types, validation attributes on request fields |
| `Infra/DbEntityRulesUnitTests` | `*DbEntity` naming/placement; nullability declared on every mapped property |
| `IntegrationEventRulesUnitTests` | integration events under `IntegrationEvents/`, implement `IIntegrationEvent`, restricted field types; listeners consume concrete events |
| `ExceptionHandlerRulesUnitTests` | every feature with exceptions has one handler + handler test class covering all of them |
| `TestsNamingConventionRulesUnitTests` | test suffix ⇔ project ⇔ base class coherence; forbidden shortcut helpers |

**Why tests and not documentation:** a rule that only lives in prose decays under delivery pressure. These classes *are* the architecture; changing a rule means changing the test and this document together.

## 7. Static Application Security Testing

- **OpenGrep** scans the repository with preset rule packs (source, docker-compose, GitHub Actions), pinned by version, run locally (`scan-changed` against the target branch) and in CI.
- Rules are tiered: **blocking** findings fail CI; **advisory** findings only report. New rules ship with test fixtures.

## 8. Nullability Gate

Nullable reference types are enabled everywhere and nullability warnings are errors — the full model, patterns, and escape-hatch discipline are in [Nullability and Nullable Reference Types](07-nullability-and-nullable-reference-types.md).

## 9. Reproducible Builds

- `Deterministic` compilation (default) plus `ContinuousIntegrationBuild=true` in CI: identical sources produce identical binaries.
- Locked-mode restore (section 4) fixes the input graph; `global.json` fixes the toolchain.

**Why:** a reproducible build makes "what exactly is running in production" answerable by hash, and makes cache poisoning detectable.

## 10. Build Performance Budget

The full local `./validate` and the CI pipeline are budgeted at **under 5 minutes** each. A gate that gets slower than the budget gets optimized or parallelized — never removed. Track the slowest stage before adding a new one.

**Why:** gates only protect if people run them; slow validation trains people to skip it.

## 11. Do / Do Not

✅ Do: run `./validate` before every push — CI should confirm, not discover.
✅ Do: fix the rule *and* the docs when a rule is genuinely wrong.
❌ Do not: lower a threshold to get a branch green.
❌ Do not: add `#pragma warning disable` / `[SuppressMessage]` without a comment linking the rationale.
❌ Do not: disable the nullability gate, the audit gate, or locked-mode restore "temporarily".

## Navigation

⬅️ Previous: [Testing Platform](05-testing-platform.md) · ➡️ Next: [Nullability and Nullable Reference Types](07-nullability-and-nullable-reference-types.md)
