# Data Persistence and Migrations

Persistence stack: **PostgreSQL**, **EF Core** (Npgsql provider), **EF Core Migrations**. The database schema is owned by migrations — the application never creates or alters schema at runtime, and the ORM mapping is *validated* against, never trusted to generate, the production schema.

## 1. Persistence Boundaries

> **Concept primer.** EF Core maps .NET classes to tables through a `DbContext`. The mapped classes must be mutable and shaped for the change tracker — constraints that would poison domain types. So the two worlds stay separate: **domain models** (immutable records) and **database entities** (`*DbEntity`, mutable classes), with explicit mapping between them.

| Concern | Location |
|---|---|
| Database entities | `src/Common/Infra/Database/Entities/<Name>DbEntity.cs` |
| Mapping configuration | `src/Common/Infra/Database/EntityConfigurations/` (`IEntityTypeConfiguration<T>`) |
| `AppDbContext` | `src/Common/Infra/Database/` |
| Domain-facing adapters | `src/Common/Infra/Adapters/` |
| Migrations | `src/Common/Infra/Database/Migrations/` (generated) |

Adapter responsibilities:

- Implement a domain port (`IUserRepositoryPort`) using the `DbContext` directly.
- Map **both ways**: domain → entity on write, entity → domain on read. Mapping methods live on the entity (`UserDbEntity.From(NewUser)`, `userDbEntity.ToUser()`).
- **Translate infrastructure exceptions into domain exceptions**: a unique-constraint violation surfaces as `UsernameAlreadyExistsException`, never as a leaked `DbUpdateException`.

Boundary rule: **domain code depends on ports, never on the `DbContext` or database entities.** Database entities never cross the Infrastructure boundary; nullability is declared explicitly on every mapped property.

**Why:** the domain stays persistence-ignorant (swappable, unit-testable); exceptions with business meaning get business names; `HexagonalArchitectureRulesUnitTests` (domain depends on no `Infra` namespace) plus `DbEntityRulesUnitTests` make leaks a failing build.

## 2. Migration Workflow

> **Concept primer.** EF Core keeps a **model snapshot** — a generated file describing the last-migrated shape of the mapping. `dotnet ef migrations add <Name>` diffs the current mapping against the snapshot and generates a migration class containing exactly the delta (plus its rollback). The schema evolves as an ordered, reviewed sequence of these migrations.

Daily loop:

1. Change entities / mapping configuration.
2. `dotnet ef migrations add <YyyyMMdd-Description>` — generates the diff-based migration.
3. **Read the generated migration** — the diff is a proposal, not a truth; verify indexes, data-loss warnings, and column types.
4. `dotnet ef database update` applies it to the local database.

Deployment applies migrations as an explicit release step — a **migration bundle** (self-contained executable produced in CI) or an idempotent SQL script for DBA-reviewed pipelines. The application does **not** call `Migrate()` at startup: multi-instance startup races and deploy-time schema surprises are not acceptable failure modes.

Local database helper (`api/db`): `applyMigrations`, `makeMigration <name>`, `backup`, `restore`, `reset` — destructive operations require interactive confirmation and take an automatic pre-operation backup.

## 3. Destructive-Migration Guard

CI scans every migration's SQL for destructive patterns (`DROP`, `DELETE`, `UPDATE`, `TRUNCATE`, column type narrowing). A destructive migration fails the pipeline **unless** it is listed in the whitelist file with a written justification.

**Why:** destructive schema changes should be rare, deliberate, and visible in review — the whitelist entry is the audit trail of that deliberateness.

**Enforced by:** `check-destructive-migrations` CI step + whitelist file (built in the [build guide](12-hands-on-build-guide.md), CI phase).

## 4. Schema Drift Detection

Three guarantees, each with a test:

1. **No un-migrated model changes:** a test fails if the current mapping differs from the migration snapshot (`Database.HasPendingModelChanges()`), i.e. someone changed an entity without adding a migration.
2. **Migrations replay from zero:** applying all migrations to a fresh containerized PostgreSQL succeeds.
3. **Mapping validates against the migrated schema:** the model builder runs against the migrated database without errors.

**Why:** ORM mapping and migration scripts are two descriptions of the same schema; these tests catch divergence the day it happens instead of the day it corrupts a deploy.

**Enforced by:** `DatabaseMigrationIntegrationTests` in `IntegrationTests`.

## 5. Soft Deletes

Soft-deleted rows (a `DeletedAt` column) combined with **partial unique indexes** (`UNIQUE … WHERE deleted_at IS NULL`) allow "delete and recreate" flows. Hardening rule: a soft delete followed by an insert of a replacement **in the same transaction** must flush the delete before the insert, or the partial index rejects the replacement. The persistence layer handles this ordering centrally (save-changes interceptor) so feature code never thinks about flush timing.

## 6. Query Visibility and Guardrails

- Every SQL statement is logged in development with its parameters and origin; queries are tagged with the calling adapter operation so any statement in the log is attributable to a line of code.
- Database-touching tests declare their expected statement count (`[AssertQueryCount]` / `[ExpectedQueries(n)]` — see [Testing Platform §8](05-testing-platform.md#8-query-count-guardrails)); an EF Core command interceptor counts and fails on mismatch.
- Adapter contract: satisfy the port with the **fewest reasonable statements** — eager-load relations you will read (`Include`), never iterate-and-query.

**Why:** N+1 patterns are invisible in functional tests and dominate real-world API latency; making the SQL footprint explicit keeps it a reviewed number.

## 7. Timestamp Precision

PostgreSQL stores timestamps at **microsecond** precision; .NET `DateTime`/`DateTimeOffset` carry 100-nanosecond ticks. A value written and read back is therefore *not equal* to the original unless truncated. The `TimeProvider` adapter truncates all produced instants to microseconds, so equality round-trips by construction. Store timestamps as `timestamptz` (UTC, `DateTimeOffset` or UTC `DateTime`) — never local time.

## 8. Do / Do Not

✅ Do: read every generated migration before committing it.
✅ Do: translate every constraint violation the domain cares about into a typed domain exception.
❌ Do not: patch schema manually outside migrations — the snapshot and reality must never diverge.
❌ Do not: expose database entities at the API boundary or inject the `DbContext` outside `Infrastructure`.
❌ Do not: call `Migrate()`/`EnsureCreated()` from application startup.

## Navigation

⬅️ Previous: [Nullability and Nullable Reference Types](07-nullability-and-nullable-reference-types.md) · ➡️ Next: [Security, Observability and Error Handling](09-security-observability-and-error-handling.md)
