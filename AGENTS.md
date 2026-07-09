be concise in your answers, sacrifice clarity for the sake of concision

# AGENTS.md

Style: telegraph; noun phrases ok; drop filler/grammar; min tokens, except for documentation which must be concise but clear and non-ambiguous.

You must read @AGENTS-GLOBAL.md for stack-agnostic instructions.

## Worktree Setup

- Freshly created worktrees: run `./init` first (renders `.env`, restores pinned tools via `dotnet tool restore`, installs git hooks). Idempotent, repo-local only.

## dotnet commands

- Format before building: `dotnet csharpier format .`. CI/verify uses check-only: `dotnet csharpier check .`.
- Full local validation = `./validate` (format → build → architecture tests → unit → integration → coverage gate → mutation). Run before every push; it is the same gate set CI runs.
- Fast signal: `dotnet build` (warnings are errors — nullability, analyzers, and code style all fail the build).
- Tests: `dotnet test` (project order: architecture → unit → integration). Integration/Testcontainers tests need Docker; request escalated permissions up front.
- Run app: `dotnet run --project src`.
- Long ops (full `dotnet test`, `dotnet stryker`): allow a generous timeout (mutation is slow); no retry unless still timing out.

## .NET Instructions

- Nullable reference types are on; nullability warnings are errors. Skip defensive null checks on non-null parameters (the contract is compiler-enforced); annotate `?` only where absence is a real outcome. See `docs/api/07-nullability-and-nullable-reference-types.md`; fix the contract, do not suppress.
- `!` (null-forgiving) is last resort: only when a runtime invariant guarantees non-null and the compiler cannot prove it; add a one-line reason comment. Prefer stronger types, `required`, pattern-matching, or flow attributes (`[MemberNotNullWhen]`, `[NotNullWhen]`).
- Immutable by default: domain models, value objects, commands/queries, events, and endpoint request/response types are `record` with `required`/`init` members; copy via `with`. No builder types.
- Collections in non-private signatures and domain fields use `System.Collections.Immutable` (`ImmutableList<T>`, `ImmutableHashSet<T>`, …). Endpoint DTOs and `*DbEntity` types may use `List<T>`/arrays (boundary exemption only).
- Argument types: smallest interface required by the callee. Return types (non-private): most precise, immutable by default; mutable only when required.
- No ambient state in domain code: no `DateTime.Now`/`UtcNow`/`Today`, `Guid.NewGuid`, or `new Random()` — inject `ITimeProviderPort` / `IRandomGeneratorPort` (enforced by `DesignRulesUnitTests`).
- Prefer `Guid` over `string` for identifiers unless serialization or an external protocol requires `string`.
- Explicit access modifiers on all members. `sealed` by default unless a type is designed for inheritance; `internal` for infrastructure not part of a domain contract.
- Constructor (primary constructor) injection only; no property/field injection, no service locator (`IServiceProvider` resolution) in business code.
- Database port adapters: satisfy the port contract with the fewest reasonable SQL statements. Inspect emitted SQL via the query logger and optimize from observed SQL (content vs count queries, `Include`s, per-row N+1), not assumptions. Prefer set-based loads, projections, entity graphs, or tailored queries over per-row queries.

## .NET Tests Instructions

- Before writing any test, read `docs/api/05-testing-platform.md` and say so in the first work update before the edit.
- Before any test edit, restate hard constraints and classify each planned test `feature-behavior` or `implementation-detail`.
- `feature-behavior` = asserts externally observable outcomes only (HTTP response, persisted state, published event, emitted log line/JSON).
- `implementation-detail` = asserts internal helper/formatter/condition behavior directly, or uses synthetic low-level framework internals.
- HARD RULE (blocker): `implementation-detail` tests forbidden.
- Unit tests are only allowed for domain code; non-domain behavior (endpoints, handlers, adapters, listeners) must be covered with integration tests.
- Coverage/mutation gaps: close with `feature-behavior` tests only; if infeasible, stop and ask for direction before adding tests.
- Use AwesomeAssertions for all assertions (`x.Should()…`); no raw `Assert.*` in behavioral tests. Assign `await act.Should().ThrowAsync<…>()`-style checks to a local Act result, blank line, then assert.
- Act pattern: exactly one `// Act` comment; blank line before and after; the statement after `// Act` triggers behavior (not an assertion); no `Given/When/Then/Arrange/Assert` comments.
- For query-count assertions, every `[AssertQueryCount]` test method needs a matching `[ExpectedQueries(n)]`.
- Endpoint / OpenAPI / security HTTP tests: use `WebApplicationFactory<Program>` (`BaseWebIntegrationTests`); do not spin up a full-DB host unless the behavior requires it, and say why if you do.
- API contract tests: keep expected response JSON inline at the assertion site (exact payload is behavior under test); use the JSON placeholder matcher only for genuinely non-deterministic fields.
- Fakes over mocks for ports reused across tests; inspection helpers live on the fake, never on the production port. Contract tests define port behavior once and run against both the fake and the real adapter.
- One test file per feature by default; per-area config via test-owned overrides, not shared test config.

## Final-response self-check

- Perform this check internally before sending the final response; do not print it by default.
- If tests were added/changed, confirm they are `feature-behavior` only.
- If the check fails, roll back the violating tests first, then rework with behavior-level tests before reporting completion.
