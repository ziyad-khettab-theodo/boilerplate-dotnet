# Feature Matrix

The capability index of the platform: every mechanism, why it matters, where it lives, and whether it exists yet. **Update the Status column as capabilities land** (`Planned` → `Built`); a capability is `Built` only when its enforcement/tests exist too. Client-specific business features are out of scope here.

## Architecture

| Capability | Why it matters | Evidence | Status |
|---|---|---|---|
| Hexagonal 3-project layout | domain physically cannot depend on frameworks | `api/src/*/…csproj` | Planned |
| Architecture rule suite | fine-grained boundaries enforced as failing tests | `tests/ArchitectureTests/` | Planned |
| Use-case convention (one `Handle`) | one operation per class, framework-free domain | `UseCaseRulesUnitTests.cs` | Planned |
| REPR endpoints (`IEndpoint`) | one class per route, endpoint-local contracts | `src/Api/Common/Endpoints/` | Planned |
| Convention DI registration of use cases | zero framework attributes in domain | `src/Api/Common/ServiceRegistration/` | Planned |
| Integration events for cross-feature flow | features stay isolated, contracts explicit | `Domain/…/IntegrationEvents/` | Planned |

## Security and Authentication

| Capability | Why it matters | Evidence | Status |
|---|---|---|---|
| Deny-by-default authorization + `/public/` convention | forgotten config yields secured, auditable surface | `src/Api/Common/Security/` + `EndpointConventionRulesUnitTests` | Planned |
| JWT in HttpOnly cookies | XSS cannot exfiltrate tokens | `src/Api/Features/Authentication/` | Planned |
| Refresh-token rotation with integrity checks | stolen refresh tokens die on reuse | authentication domain service | Planned |
| Management endpoints behind basic auth | topology/version info never anonymous | `/managementz` pipeline | Planned |
| CORS hardening via per-env origins | no wildcard in deployed envs | startup validation | Planned |

## Data

| Capability | Why it matters | Evidence | Status |
|---|---|---|---|
| Domain model / DbEntity separation | persistence constraints never shape the domain | `Infrastructure/Database/Entities/` | Planned |
| Migration-owned schema (`ddl` never at runtime) | reviewed, replayable schema history | `Infrastructure/Database/Migrations/` | Planned |
| Destructive-migration CI guard + whitelist | destructive SQL is deliberate and audited | CI step + whitelist file | Planned |
| Schema drift tests | mapping and migrations cannot diverge silently | `DatabaseMigrationIntegrationTests` | Planned |
| Query-count guardrails | N+1 regressions fail tests | `[AssertQueryCount]` interceptor | Planned |
| Timestamp precision truncation | DB round-trip equality by construction | `TimeProvider` adapter | Planned |

## Errors, Observability

| Capability | Why it matters | Evidence | Status |
|---|---|---|---|
| ProblemDetails everywhere + stable `errors.*` codes | machine-consumable, contract-stable errors | `src/Api/Common/ExceptionHandling/` | Planned |
| Layered exception handlers (map-only) | every exception has one tested mapping | feature `ExceptionHandlers/` + rules test | Planned |
| Event-driven logging + audit pipeline | business facts observable, PII-safe audit trail | audit feature + `IAuditSinkPort` | Planned |
| Structured JSON logs + trace correlation | queryable logs joined to traces via `X-Trace-Id` | logging setup + middleware | Planned |
| OTel traces/metrics/logs via OTLP | vendor-neutral telemetry, local = prod pipeline | OTel wiring in `Program.cs` | Planned |
| Local Grafana observability stack | see your own traces while coding | `docker-compose.dependencies.yml` | Planned |
| SLO defaults + instrumented SLIs | reliability decisions by arithmetic | [doc 09 §8.8](09-security-observability-and-error-handling.md#88-slos-and-error-budgets) | Planned |

## Build, Quality, Supply Chain

| Capability | Why it matters | Evidence | Status |
|---|---|---|---|
| Zero-warning build (analyzers `latest-all`) | warnings never accumulate | `Directory.Build.props` | Planned |
| NRT everywhere, diagnostics as errors | null bugs die at compile time | `Directory.Build.props` | Planned |
| Banned ambient APIs in Domain | deterministic domain | `BannedSymbols.txt` | Planned |
| CSharpier + staged pre-commit hook | zero format drift, zero debate | `lefthook.yml` + `.tools` scripts | Planned |
| Coverage gates 95/80 (+ Domain 95) | untested code can't merge | ReportGenerator gate in `./validate`/CI | Planned |
| Mutation gates 95/98 on Domain | tests proven to detect behavior change | `stryker-config.json` | Planned |
| Template-100% threshold profile | template stays exemplary, clients start at baseline | name-conditioned MSBuild props | Planned |
| CPM + lock files + locked restore | reviewed dependency graph, reproducible restore | `Directory.Packages.props`, `packages.lock.json` | Planned |
| NuGetAudit as CVE gate | high/critical advisories fail the build | `NU1903;NU1904` as errors | Planned |
| Deterministic/CI builds | binary ↔ source traceability | `ContinuousIntegrationBuild` | Planned |
| OpenGrep SAST (blocking/advisory tiers) | security patterns gate merges | `.tools/opengrep/` + CI | Planned |
| Renovate + daily full audit | updates flow in, advisories page out | `.ci/renovate/` + scheduled workflows | Planned |
| Pinned actions (SHA) and images (digest) | supply-chain tamper resistance | OpenGrep rules | Planned |

## Testing

| Capability | Why it matters | Evidence | Status |
|---|---|---|---|
| Test taxonomy + naming enforcement | category ⇔ project ⇔ base class coherence | `TestsNamingConventionRulesUnitTests` | Planned |
| Fakes + port contract tests | fakes provably equivalent to real adapters | `TestHelpers` + `*ContractTests` | Planned |
| Full-host application tests via `Program.cs` | composition root under test | `BaseApplicationTestsWithDb` | Planned |
| Testcontainers PostgreSQL (compose-image parity) | tests run the real database version | database fixture | Planned |
| JSON assertions with typed placeholders | inline, readable API contracts | `TestHelpers` JSON helpers | Planned |
| Act-pattern convention | uniformly scannable tests | [doc 05 §9](05-testing-platform.md#9-the-act-pattern) (analyzer: roadmap) | Planned |
| Random test order + logged seed, no retries | hidden coupling surfaced, flakes not hidden | runner config | Planned |

## CI/CD, Governance

| Capability | Why it matters | Evidence | Status |
|---|---|---|---|
| Layered workflows + change detection | shared shape, fast docs-only PRs | `.github/workflows/all*.yml` | Planned |
| Single required check ("Workflow end marker") | governance config that doesn't rot | `default_branch_ruleset.json` | Planned |
| Container health + startup smoke job | image and boot path validated per PR | `compose-health-and-startup` | Planned |
| Template-only digest regression workflow | test-suite regressions in the template caught | `boilerplate-tests.yml` | Planned |
| Branch ruleset (approval, linear, squash/rebase) | reviewed, replayable history | `.github/default_branch_ruleset.json` | Planned |

## Navigation

⬅️ Previous: [CI/CD and Governance](10-ci-cd-and-governance.md) · ➡️ Next: [Hands-On Build Guide](12-hands-on-build-guide.md)
