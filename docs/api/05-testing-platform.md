# Testing Platform

Testing is treated as an **architecture concern** in this project. The test suite is not a final-stage check: architecture rules run as tests, the domain's executable specification lives in unit tests, and the platform ships shared helpers so tests stay readable and cheap to write. Weak test infrastructure is a defect of the platform, not of the person writing the test.

## 1. Strategy

- **Exhaustive unit tests on the domain** — use cases and value objects are the specification of business behavior.
- **Mutation testing** on the domain-heavy scope proves the tests actually detect behavior changes (see [Build Toolchain and Quality Gates](06-build-toolchain-and-quality-gates.md#3-coverage-and-mutation-thresholds)).
- **Fakes preferred over repeated mocks** for dependencies used across many tests.
- **Contract tests** define required port behavior once, run against every implementation.
- **Focused integration tests** on boundaries (API↔domain, domain↔infrastructure).
- **Isolated tests for cross-cutting concerns** — security, exception handling, logging.
- **Application/flow tests** for multi-step scenarios through the fully booted host.

### 1.1 Which Test Should I Add?

| Change | Minimum test layer |
|---|---|
| New business branch in a use case / value object | Unit tests on the use case or value object |
| New driven adapter (new port) | Contract tests + adapter integration tests with query-count assertions |
| New adapter for an **existing** port | Extend nothing — run the existing contract tests against it + adapter integration tests |
| Port contract change | Update the abstract contract tests first, then fix every implementation until green |
| Endpoint added/changed | Web integration test (status, payload, authorization) via `BaseWebIntegrationTests` |
| New/changed exception → API mapping | Exception handler integration tests (status + ProblemDetails payload) |
| Scheduled task change | `BaseScheduledTaskIntegrationTests` |
| Multi-step business scenario | Application/flow tests |

Contract tests define **shared domain expectations**; abstract contract classes must not contain implementation-specific assertions.

## 2. Test Categories and Naming

Test **class** suffixes define the category; architecture tests enforce them:

| Suffix | Category | Project | Boots |
|---|---|---|---|
| `*RulesUnitTests` | architecture rules | `ArchitectureTests` | nothing (assembly analysis) |
| `*UnitTests` | domain unit tests | `Domain.UnitTests` | nothing (plain constructors + fakes) |
| `*IntegrationTests` | boundary tests | `Api.IntegrationTests` | partial host / database |
| `*ApplicationTests` | full application tests | `Api.IntegrationTests` | full host via `Program.cs` |
| `*FlowApplicationTests` | end-to-end user journeys | `Api.IntegrationTests` | full host |
| `*ContractTests` | abstract port contracts | `TestHelpers` (abstract) + concrete per implementation | depends on implementation |

**Execution order:** architecture tests → unit tests → integration/application tests. The `./validate` script and the CI test job run the projects in that order and stop at the first failing stage.

**Why:** fail fast on structural violations — there is no point debugging an integration failure caused by a misplaced class.

**Enforced by:** `tests/ArchitectureTests/TestsNamingConventionRulesUnitTests.cs` (suffix ⇔ project ⇔ base class coherence).

## 3. Unit Test Scope

- Primarily **use cases** and **value objects with validation**.
- Test through the public domain contract (`Handle`, constructors/factories) — never through internals.
- Unit tests are allowed **only for domain code**. Framework-dependent behavior (endpoints, handlers, adapters, listeners) is covered by integration tests — mocking the framework proves nothing about production behavior.

> Unit tests are the main executable specification of business behavior. Read them as documentation; write them so they can be read as documentation.

## 4. Fakes over Mocks

> **Concept primer.** A **mock** is configured per test ("when `FindByUsername` is called, return X") — setup noise repeated everywhere, coupled to call details. A **fake** is a small working implementation (an in-memory repository backed by a dictionary) written once, shared by all tests, and exercised like the real thing.

Rules:

- A dependency used in many tests gets a **fake** in `TestHelpers` (e.g. `FakeUserRepository`, `FakeTimeProvider`, `FakeEventPublisher`, `FakePasswordEncoder`, `FakeRandomGenerator`).
- Fakes implement the port — the same contract tests run against fake and real adapters, so a fake cannot drift from production behavior silently.
- **Do not add members to production ports only to make testing easier.** If tests need inspection helpers (`PublishedEvents`, `Seed(...)`), put them on the fake class itself.
- Fakes are stateful by design; every test gets a fresh instance (xUnit creates a new test-class instance per test; integration test base classes reset registered fakes per test).

**Why:** fakes make tests read as behavior ("a user already exists with this name") instead of mechanics ("this method returns that value"), and are less brittle under refactoring.

## 5. Port Contract Tests

One specification, many implementations:

```text
UserRepositoryPortContractTests        (abstract — the behavioral spec, in TestHelpers)
├── FakeUserRepositoryContractTests    (runs the spec against the fake, in Domain.UnitTests)
└── UserRepositoryContractTests        (runs the spec against the real adapter + database,
                                        in Api.IntegrationTests)
```

- The abstract class declares the required behavior of the port (`creating a user with a taken username throws UsernameAlreadyExistsException`).
- Each implementation provides only the setup; assertions live in the abstract class.

**Why:** this is what makes fakes trustworthy and adapters swappable — the contract is executable, not tribal knowledge.

## 6. What "Integration Test" Means Here

Integration tests validate **boundaries**, not "the host starts":

- **API ↔ Domain:** request mapping/validation, authorization metadata, response mapping.
- **Domain ↔ Infrastructure:** adapter behavior, persistence semantics, exception translation.
- **Cross-cutting runtime behavior:** exception handler payloads, security chains, structured logging.

A test that boots half the application to assert something a unit test could assert is a smell; a unit test that mocks three layers to simulate a boundary is a worse one.

## 7. Test Infrastructure Building Blocks

All in `tests/TestHelpers` unless noted. Each exists to keep individual tests short and intention-revealing.

| Block | What it provides | Why it exists |
|---|---|---|
| `BaseWebIntegrationTests` | in-memory host (`WebApplicationFactory<Program>`), fakes registered over real adapters, `GetAccessTokenCookie(...)` helpers, JSON assertion helpers | endpoint tests exercise the real HTTP pipeline (routing, validation, auth, serialization) without a database |
| `BaseApplicationTestsWithDb` | full host boot through `Program.cs` + a containerized PostgreSQL (Testcontainers), one isolated schema per test | catches composition-root and configuration regressions a sliced host hides |
| `BaseApplicationTestsWithoutDb` | full host with repositories substituted | application-level tests that don't need persistence semantics |
| `BaseScheduledTaskIntegrationTests` | harness to trigger a scheduled task deterministically | scheduled tasks are driving adapters and deserve the same boundary tests as endpoints |
| `BaseExceptionHandlerIntegrationTests` | throws registered exceptions through the pipeline, asserts status + ProblemDetails | the error contract is API surface; every exception mapping is proven |
| `BaseIntegrationEventPublisherIntegrationTests` | asserts domain event → integration event conversion | keeps cross-feature contracts honest |
| JSON assertion helpers | expected-JSON comparison with typed placeholders — `<<userId:UUID>>` (capture + format check), `<<:NotEmpty>>`, repeated-capture consistency; strict or order-ignoring array modes | API contract tests keep the full expected JSON inline and readable, without brittleness on generated values |
| Fixture helpers | `AUser()`, `ANewUser(...)`-style factories with irrelevant values defaulted | tests state only what matters for the behavior under test |
| Testcontainers database fixture | starts PostgreSQL from the same image tag as `docker-compose.dependencies.yml` | tests and local stack cannot drift to different database versions |

Configuration layering for full-host tests: tests boot through the production `Program.cs` (never a parallel test composition), with a test-only configuration layer applied on top; per-test toggles use the host builder's configuration overrides, not copies of production settings.

## 8. Query-Count Guardrails

Database-touching adapter tests declare their expected SQL footprint:

- A test class opting in via `[AssertQueryCount]` requires an `[ExpectedQueries(n)]` declaration on **every** test method; an EF Core command interceptor counts actual statements and fails on mismatch.

**Why:** N+1 regressions and hidden query drift are invisible in green tests and expensive in production. The declaration also documents the intended footprint of every port operation.

✅ Do: treat a query-count change as a contract change — update the number consciously, in review.
❌ Do not: skip the assertion on a database-sensitive adapter test because the count "might change".

## 9. The Act Pattern

Every test marks its action — the single line that triggers the behavior under test — with one `// Act` comment:

```csharp
[Fact]
public async Task Rejects_signup_when_username_already_exists()
{
    var sut = SignupSut.WithExistingUser(AUser(username: "ziyad"));

    // Act
    var act = () => sut.UseCase.Handle(ASignupCommand(username: "ziyad"), CancellationToken.None);

    await act.Should().ThrowAsync<UsernameAlreadyExistsException>();
    sut.EventPublisher.PublishedEvents.Should().ContainSingle(e => e is UserSignupRejectedEvent);
}
```

Rules: exactly one `// Act` per test; the next statement triggers behavior (not an assertion); blank lines around the act block; no `Given/When/Then/Arrange/Assert` phase comments.

**Why:** a uniform visual anchor makes any test scannable in seconds — everything above is setup, everything below is verification. Phase-comment prose rots; one marker doesn't.

**Enforced by:** review + test conventions check (a dedicated analyzer is on the roadmap — see [Feature Matrix](11-feature-matrix.md)).

## 10. Exception Testing Model

Every domain exception is tested at **two levels**:

1. **Behavior level** (unit/contract tests): the condition raises the right exception with the right context.
2. **API payload level** (handler integration tests): the exception maps to the right HTTP status and stable `errors.*` code in the ProblemDetails body.

**Enforced by:** `tests/ArchitectureTests/ExceptionHandlerRulesUnitTests.cs` — every feature that declares exceptions must have a handler test class covering all of them.

## 11. Determinism and Anti-Flake Policy

- Test execution order is randomized with a **logged seed**; a fixed seed can be set to reproduce an ordering-dependent failure.
- **No automatic retries.** A test that passes only on rerun is a failing test: it has hidden state coupling or a real race. Fix it or delete it — never quarantine it.
- Time, randomness, and IDs come from fakes (`FakeTimeProvider`, `FakeRandomGenerator`) — a test that depends on the wall clock is wrong by construction (and the domain cannot reach the wall clock anyway).

**Why:** random order actively hunts inter-test coupling; retries would hide exactly the bugs the suite exists to find.

## 12. Do / Do Not

✅ Do: add fixture and assertion helpers when a pattern repeats — readable tests are a platform feature.
✅ Do: keep expected JSON inline in API contract tests (with placeholders), so the contract is visible in the test.
❌ Do not: widen production visibility (make members public/internal) only so tests can reach them.
❌ Do not: duplicate full business-branch coverage at the endpoint layer — endpoints get mapping/authorization tests; branches are unit-tested in the domain.
❌ Do not: assert implementation details (which methods were called, in what order) — assert externally observable outcomes: response, persisted state, published event, emitted log.
❌ Do not: write unit tests for non-domain code — framework behavior gets integration tests.

## Navigation

⬅️ Previous: [Project Bootstrap Checklist](04-project-bootstrap-checklist.md) · ➡️ Next: [Build Toolchain and Quality Gates](06-build-toolchain-and-quality-gates.md)
