# Project Bootstrap Checklist

Everything to take this template from clone to a production client project. Work top to bottom; each step is idempotent and reviewable.

## 1. Make It Yours: Rename

- [ ] Rename the solution, projects, root namespace, and folders from `Theodo.DotnetBoilerplate` to `<Client>.<Project>` (`dotnet` has no rename command — IDE refactor + find/replace over `.slnx`, `.csproj`, `Program.cs`, `Directory.Build.props`).
- [ ] Update `README.md` (title, description) and container image names in compose files.
- [ ] Regenerate lock files after the rename (`dotnet restore --force-evaluate`).

**Threshold side effect (intentional):** the 100% coverage/mutation thresholds are conditioned on the `Theodo.DotnetBoilerplate` project-name prefix. Renaming automatically drops the gates to the baseline (95/80/95/98 — see [toolchain §3](06-build-toolchain-and-quality-gates.md#3-coverage-and-mutation-thresholds)). Set stricter values in `Directory.Build.props` if your project wants them.

## 2. CI/CD and Governance

- [ ] CI green on the first post-rename commit (all gates, no skips).
- [ ] Apply the branch ruleset (`.github/default_branch_ruleset.json`) to the default branch.
- [ ] Add a `CODEOWNERS` file naming the owning team.
- [ ] **Remove the template-only regression workflow** (`boilerplate-tests.yml`) — see [CI/CD §6](10-ci-cd-and-governance.md#6-template-only-regression-protection).
- [ ] Configure Renovate credentials (`RENOVATE_TOKEN`).

## 3. Database and Secrets

- [ ] Provision PostgreSQL per environment; set `ConnectionStrings__Database` from the platform secret store — never in files, never in CI variables shared across environments.
- [ ] Verify migrations run as a release step (bundle or idempotent script — [persistence §2](08-data-persistence-and-migrations.md#2-migration-workflow)).
- [ ] Startup fails fast on missing configuration (options validation) — deploy once with a secret missing on purpose and confirm the crash message names the option.

## 4. Runtime Identity, Environments, Observability

- [ ] Set `ASPNETCORE_ENVIRONMENT` (framework behavior) and `APP_ENVIRONMENT` = `dev` / `staging` / `prod` (telemetry identity) per environment.
- [ ] Set `OTEL_SERVICE_NAME`; point OTLP export (`OTEL_EXPORTER_OTLP_ENDPOINT` / `_HEADERS`) at your telemetry backend.
- [ ] Wire error tracking to the team's alerting channel.
- [ ] **Ratify SLOs with the client** (start from the [template defaults](09-security-observability-and-error-handling.md#88-slos-and-error-budgets)) and wire burn-rate alerts.

## 5. Security Hardening (before anything public)

- [ ] `Security:Https=true`; real `Security__Jwt__Secret` / `Security__Jwt__Issuer` from the secret store.
- [ ] CORS origins pinned per environment — no wildcards outside local dev.
- [ ] Management credentials (`Management__User`/`Management__Password`) set; management endpoints kept off the public edge where the platform allows.
- [ ] OpenAPI exposure off outside Development.
- [ ] Update `SECURITY.md` contact and scope for the client project.
- [ ] Full list: [production hardening checklist](09-security-observability-and-error-handling.md#9-production-hardening-checklist).

## 6. Go-Live Exit Criteria

- [ ] All secrets from the platform store; none in the repo history.
- [ ] Health checks wired into the platform's probes; alerting tested end to end (break something in staging, watch the page arrive).
- [ ] SLOs ratified; error budget policy agreed.
- [ ] Runbook linked from the README (deploy, rollback, migration failure, secret rotation).
- [ ] `./validate` and CI equivalent and green.

## Navigation

⬅️ Previous: [Project Structure and Conventions](03-project-structure-and-conventions.md) · ➡️ Next: [Testing Platform](05-testing-platform.md)
