# Developer Guide

The engineering reference for this .NET 10 REST API. It is **intentionally opinionated**: explicit rules, automated enforcement, and repeatable patterns over team-by-team interpretation. The goal is simple — any engineer should be able to modify the project safely without guessing local conventions.

## Core Principles

- ✅ Business logic lives in domain code, never in adapters.
- ✅ Tests are architecture, not a final-stage check.
- ✅ Every rule is enforced by tooling (compiler, analyzers, architecture tests, CI gates).
- ❌ No one-off bypasses: to deviate, change the rule *and* its docs with rationale — or comply.

Non-negotiable examples: one use case class exposes one `Handle` operation; every endpoint declares its authorization explicitly and only `/public/` routes may be anonymous; tests assert observable behavior, never implementation details.

The two biggest mistakes to avoid: (1) business logic drifting into endpoints/adapters because they are "near"; (2) weak test-helper design making tests unreadable and expensive.

## Reference Map

### Start Here

| Doc | What it gives you |
|---|---|
| [1 — Onboarding Guide](01-onboarding-guide.md) | clone → running API → first architecture trace |
| [2 — Architecture Overview](02-architecture-overview.md) | the architectural contract: layers, dependency rules, building blocks |
| [3 — Project Structure and Conventions](03-project-structure-and-conventions.md) | where everything lives and what it is called |

### Project Kickoff

| Doc | What it gives you |
|---|---|
| [4 — Project Bootstrap Checklist](04-project-bootstrap-checklist.md) | template → production client project |

### Engineering and Platform Reference

| Doc | What it gives you |
|---|---|
| [5 — Testing Platform](05-testing-platform.md) | test taxonomy, fakes, contract tests, guardrails |
| [6 — Build Toolchain and Quality Gates](06-build-toolchain-and-quality-gates.md) | every gate, every threshold, failure triage |
| [7 — Nullability and Nullable Reference Types](07-nullability-and-nullable-reference-types.md) | the null-safety gate and its patterns |
| [8 — Data Persistence and Migrations](08-data-persistence-and-migrations.md) | EF Core boundaries, migration workflow, drift guards |
| [9 — Security, Observability and Error Handling](09-security-observability-and-error-handling.md) | auth model, error contract, telemetry, SLOs |
| [10 — CI/CD and Governance](10-ci-cd-and-governance.md) | pipeline topology, branch rules, supply chain |
| [14 — C# Language and Async Conventions](14-csharp-language-and-async-conventions.md) | how the C# is written: casing, record/sealed/property idioms, the async rulebook, DI lifetimes |

### Full Capability Index

| Doc | What it gives you |
|---|---|
| [11 — Feature Matrix](11-feature-matrix.md) | every platform capability: why, where, status |

### Building and Migrating

| Doc | What it gives you |
|---|---|
| [12 — Hands-On Build Guide](12-hands-on-build-guide.md) | construct the entire platform yourself, file by file, gate by gate |
| [13 — Guide for Java/Spring Developers](13-guide-for-java-spring-developers.md) | coming from the JVM? the translation layer |

## Rare but Important Mechanisms

| When you hit… | Read |
|---|---|
| a query-count assertion failing | [Testing Platform §8](05-testing-platform.md#8-query-count-guardrails) |
| an architecture test naming a rule you don't know | [Toolchain §6](06-build-toolchain-and-quality-gates.md#6-architecture-test-inventory) |
| a nullability diagnostic you can't place | [Nullability §4](07-nullability-and-nullable-reference-types.md#4-common-error-patterns-and-fixes) |
| a destructive-migration CI failure | [Persistence §3](08-data-persistence-and-migrations.md#3-destructive-migration-guard) |
| an `errors.*` code change request | [Security/Errors §7](09-security-observability-and-error-handling.md#7-error-contract) |
| thresholds differing between template and client | [Toolchain §3](06-build-toolchain-and-quality-gates.md#3-coverage-and-mutation-thresholds) |

## Path Legend

- `src/Features/<subdomain>/{Domain,Api}/…`, `src/Common/{Api,Domain,Infra,Utils}/…` → under `api/src/` (one application project)
- `tests/…` → under `api/tests/`
- Docs cite target paths; while the platform is under construction, the [Feature Matrix](11-feature-matrix.md) Status column says what exists.

## Navigation

➡️ Next: [Onboarding Guide](01-onboarding-guide.md)
