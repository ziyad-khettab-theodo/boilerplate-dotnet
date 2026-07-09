# Security, Observability and Error Handling

This document defines the runtime contracts of the API: how requests are authenticated and authorized, how errors reach clients, and how the system explains itself in logs, traces, and metrics.

## 1. Security Posture

- **Stateless.** No server-side sessions; every request authenticates via a signed JWT. Any instance can serve any request — horizontal scaling and restarts are safe by construction.
- The request pipeline treats routes in three classes:
  1. **Public routes** — path contains `/public/`: anonymous by explicit declaration.
  2. **API documentation routes** — mapped only when OpenAPI exposure is enabled (off outside Development).
  3. **Everything else** — requires a valid JWT; the deny-by-default fallback policy guarantees this even for endpoints that forgot to declare anything.

## 2. Authorization Contract

Deny-by-default, declared explicitly, path-signaled — the full contract with examples lives in [Architecture Overview §6.1](02-architecture-overview.md#61-authorization-rule). Operationally:

- The fallback policy (`RequireAuthenticatedUser`) covers anything unmapped by intent.
- Every endpoint declares `.RequireAuthorization(<policy>)` or `.AllowAnonymous()`; the architecture test rejects endpoints without explicit metadata and anonymous endpoints outside `/public/` paths.
- Role/permission checks use **named policies** (`Policies.Admin`), never inline role strings scattered across endpoints.

## 3. CORS

- Development default is permissive to keep local frontend work friction-free.
- **Hardening rule:** allowed origins are injected per environment (`Security__Cors__AllowedOrigins`) — production must never run with wildcard origins, and credentials + wildcard is rejected by validation at startup.

## 4. JWT and Cookie Transport

Tokens travel in **HttpOnly cookies**, not in JavaScript-readable storage:

| Cookie | Content | Path scope | Lifetime |
|---|---|---|---|
| `accessToken` | short-lived JWT (claims: subject, roles, expiry) | `/api` | minutes |
| `refreshToken` | opaque rotation token | refresh + logout endpoints only | days |

- Cookies are `HttpOnly` (XSS cannot read them), `SameSite=Strict` (CSRF mitigation), and `Secure` whenever `Security:Https=true` (any deployed environment).
- The authentication handler reads the bearer token from the `accessToken` cookie; the access-token cookie's max-age is aligned with the refresh-token validity so the browser drops both together.
- Signing: symmetric key from configuration (`Security__Jwt__Secret`, `Security__Jwt__Issuer`) — injected per environment, never committed.

**Why cookies:** an SPA storing JWTs in `localStorage` hands them to any successful XSS. HttpOnly cookies remove that entire class; `SameSite=Strict` plus explicit path scoping addresses the CSRF trade-off.

## 5. Token Lifecycle Integrity

Refresh is **rotation with integrity checks**, implemented as a domain service used by the login/refresh/logout use cases:

- A refresh request validates that the refresh token exists and is unexpired, that the access/refresh pair belong to the **same user**, and **invalidates the previous refresh token** on rotation.
- Consequences: a stolen refresh token dies on first legitimate reuse (replay detection); mismatched token pairs are rejected; logout invalidates the session server-side.

**Why:** refresh tokens are long-lived credentials; rotation-with-invalidation is the standard containment for their theft.

## 6. Management Endpoints

Operational endpoints (liveness/readiness health checks, build info) live under `/managementz`, **outside** the business API surface:

- Protected by HTTP Basic auth with per-environment credentials (`Management__User` / `Management__Password`) — they leak topology and versions, so they are never anonymous.
- The container healthcheck probes a management endpoint and treats **401 as alive** (the process answered; credentials aren't the probe's business).

## 7. Error Contract

> **Concept primer.** RFC 9457 **Problem Details** is the standard JSON error shape (`type`, `title`, `status`, `detail`, `instance` + extensions). ASP.NET Core supports it natively; this project makes it the only error shape any client ever sees.

- Every error response is a ProblemDetails document.
- Each error case carries a **stable code** in the `errors.*` namespace (`errors.username_already_exists`, `errors.unauthorized`, `errors.validation`). **Stable means: the same error case maps to the same code forever.** Clients program against these codes; changing one is an API contract change with a migration plan — never a refactor.
- Validation failures return `errors.validation` with a per-field breakdown in the standard `errors` extension.

### 7.1 Exception Handler Layering

Exceptions become responses through an ordered handler chain (`IExceptionHandler` implementations in `Api`):

| Order | Handler | Maps |
|---|---|---|
| 1 | framework handler | validation failures, malformed bodies, authN/authZ failures → 400/401/403 with stable codes |
| 2 | feature handlers (`Features/<subdomain>/ExceptionHandlers/`) | each feature's domain exceptions → status + `errors.*` code |
| 3 | catch-all handler | anything unmapped → 500 `errors.internal`, full exception logged, **no internals in the body** |

Feature handlers are **mapping tables, not logic**: exception type → (status, code). No business decisions, no repository calls, no event publishing inside handlers. Every mapped exception must appear in the feature's handler integration tests (enforced by `ExceptionHandlerRulesUnitTests`).

Unhandled exceptions additionally publish an `UnhandledExceptionEvent` so logging/alerting observe them uniformly (see 8.1).

### 7.2 Decision: typed exceptions, not Result types

**This project models *all* domain failures — expected and unexpected — as typed `DomainException` subclasses**, mapped to ProblemDetails by the handler chain above. It does **not** use a Result type (`ErrorOr`, `OneOf`, `FluentResults`, `Result<T>`) for expected failures.

**Alternative considered.** The mainstream 2026 counter-pattern splits failures: *expected* business failures (validation, not-found, "username already taken") return a `Result<T>`/`ErrorOr<T>` — failure explicit in the signature — while only *unexpected* failures throw. It makes failure paths visible in the type system and avoids control-flow-by-exception.

**Why exceptions here.**

- One uniform path to the wire: every failure already funnels through the `IExceptionHandler` chain into ProblemDetails. Result types would need a parallel mapping at each endpoint.
- Explicit failure modeling is already required *without* a Result type: the [failure-reason-enum rule](../../AGENTS-GLOBAL.md) forces each failure exception to carry a stable reason, so failures are still modeled, not stringly-typed.
- Less ceremony for the target audience (developers and AI agents following one pattern), and it mirrors the reference architecture.

**Cost accepted.** Failure is not visible in method signatures ("invisible control flow"), and exceptions carry a small performance cost on the throw path — acceptable because expected-failure throws are not hot-path.

**Revisit if** a feature accumulates many expected-failure branches per operation (where a Result type reads better), or profiling shows exception-throw cost on a hot path. If adopted, standardize on **one** library (ErrorOr is the pragmatic ASP.NET choice) rather than mixing. Language-level async/exception rules are in [C# Language and Async Conventions §3.5](14-csharp-language-and-async-conventions.md#35-concurrency-and-exceptions).

## 8. Observability

### 8.1 Event-Driven Logging and Audit

Domain code never calls a logger — it publishes **domain events** through `IEventPublisherPort`; a logging listener in `Api` writes them with source attribution. This keeps the domain framework-free and makes every business fact observable by default.

**Audit pipeline** (strict boundary chain, no shortcuts):

```text
use case ──publishes──► domain event
   feature publisher ──converts──► integration event (stable, versionable contract)
      audit listener ──consumes──► RecordAuditUseCase (audit feature's domain)
         IAuditSinkPort ──implemented by──► audit sink adapter (dedicated "AUDIT" logger → stdout/OTLP)
```

Audit rules:

- Audit records follow a fixed schema: `occurredAt`, `action`, `outcome`, `actor`, `resource`, `request`, `sourceEvent`, `reason`.
- **Never** include cookies, authorization headers, tokens, passwords, or raw headers in audit records; `reason` uses stable codes, never raw exception messages.
- Downstream audit storage is append-only.

### 8.2 SQL Observability

Statement logging, origin tagging, and query-count guardrails — see [Data Persistence and Migrations §6](08-data-persistence-and-migrations.md#6-query-visibility-and-guardrails).

### 8.3 OpenAPI and Serialization

- The OpenAPI document is generated from code (endpoints, records, XML comments) and served together with an interactive UI **only when enabled** (`OpenApi:Enabled` — on in Development, off by default elsewhere).
- JSON serialization is **strict**: unknown members in a request body are rejected, not ignored; no silent string↔number coercion. Malformed input fails fast at the boundary with `errors.validation` rather than propagating half-bound objects.

### 8.4 Structured Logging and Trace Correlation

- Console output is **structured JSON** by default (plain text only in local development). Fixed field contract: `timestamp`, `severity`, `message`, `category`, `service.name`, `deployment.environment`, `trace_id`, `span_id`, `exception.*`.
- Every log line written during a request carries the active trace context; responses include an `X-Trace-Id` header so a user-reported error can be joined to its logs and trace in one lookup.

**Why:** logs are queried by machines; JSON with stable fields makes "all errors for trace X in env Y" a filter, not a regex safari.

### 8.5 Tracing

- **OpenTelemetry** end to end: incoming HTTP, outbound HTTP, and database commands produce spans; export via OTLP (off by default, on in local development toward the local stack; endpoint/headers via standard `OTEL_*` variables in deployed environments).
- Span error semantics follow OTel conventions: server spans are `ERROR` for 5xx responses; 2xx/4xx leave status `UNSET`; exception attributes are recorded only when a real exception exists — a non-exception 5xx must not synthesize fake exception events.
- Trace context propagates across async boundaries and into background work started by a request.

### 8.6 Metrics

- OTel semantic-convention meters: HTTP server duration histograms (with SLO-aligned buckets from 50 ms to 60 s), runtime metrics (GC, thread pool, allocations), and database metrics.
- Base time unit: seconds. Custom business metrics use the same meter API and naming conventions.

### 8.7 Local Observability Stack

`docker compose up -d` starts an all-in-one Grafana/Tempo/Loki/Prometheus container next to PostgreSQL: Grafana on `:3333`, OTLP intake on `:4317`/`:4318`. Local development exports everything there — you can see your own traces and logs while coding, so observability regressions are caught at development time.

### 8.8 SLOs and Error Budgets

> **Concept primer.** An **SLI** is a measured indicator (fraction of non-5xx responses). An **SLO** is the target on it (99.9% over 30 days). The **error budget** is the allowed failure volume (0.1%); while budget remains you ship normally, when it's exhausted you freeze risky changes and fix reliability. This replaces "is it stable enough?" debates with arithmetic.

Template defaults (ratify per project at bootstrap):

| SLO | Target |
|---|---|
| Availability | ≥ 99.9% non-5xx responses, 30-day window |
| Latency | p95 < 250 ms, p99 < 1 s |

The SLIs for both are already instrumented (8.5/8.6); burn-rate alerts are wired during bootstrap ([checklist §4](04-project-bootstrap-checklist.md)).

## 9. Production Hardening Checklist

Before first production deploy (also part of the [bootstrap checklist](04-project-bootstrap-checklist.md)):

- [ ] `Security:Https=true` (Secure cookies, HSTS)
- [ ] Real `Security__Jwt__Secret` / `Security__Jwt__Issuer` from the platform secret store
- [ ] CORS origins pinned per environment
- [ ] Management credentials set; management endpoints unreachable from the public edge where possible
- [ ] OpenAPI exposure disabled
- [ ] OTLP export configured; error tracking wired; burn-rate alerts tested

### 9.1 Example Production Overrides

Split by sensitivity. **Non-secret** production toggles live in `appsettings.Production.json` (committed, loaded when `ASPNETCORE_ENVIRONMENT=Production`):

```json
{
  "Security": {
    "Https": true,
    "Jwt": { "Issuer": "https://auth.example.com" },
    "Cors": { "AllowedOrigins": [ "https://app.example.com", "https://admin.example.com" ] }
  },
  "OpenApi": { "Enabled": false }
}
```

**Secrets never go in a config file** — even as placeholders. They arrive as environment variables from the platform secret store, overriding config keys via the `__` separator (env vars win over `appsettings*.json`):

```bash
Security__Jwt__Secret=<from secret manager>
Management__User=<from secret manager>
Management__Password=<from secret manager>
ConnectionStrings__Database=<from secret manager>
OTEL_EXPORTER_OTLP_ENDPOINT=https://otel-collector.example.com
```

Startup validation (`ValidateOnStart`) fails fast if any required key is absent from both layers — so a missing secret is a crash at boot, not a runtime surprise (see [§8.3](#83-openapi-and-serialization) and the options wiring in [Hands-On Build Guide §5.3](12-hands-on-build-guide.md#53-strict-json--validated-options)).

## 10. Do / Do Not

✅ Do: add a stable `errors.*` code the moment you add a domain exception — with its handler mapping and handler test.
✅ Do: treat a change to any `errors.*` code as an API-breaking change.
❌ Do not: use `.AllowAnonymous()` as a convenience shortcut — public is a path-visible decision.
❌ Do not: return ad-hoc error payloads from an endpoint — everything goes through the handler chain.
❌ Do not: log tokens, cookies, passwords, or raw authorization headers — anywhere, audit included.
❌ Do not: keep development-grade CORS or secrets in a deployed environment.

## Navigation

⬅️ Previous: [Data Persistence and Migrations](08-data-persistence-and-migrations.md) · ➡️ Next: [CI/CD and Governance](10-ci-cd-and-governance.md)
