# CI/CD and Governance

The pipeline is the enforcement arm of every rule in this guide: what `./validate` checks locally, CI checks authoritatively, and branch protection makes unbypassable. CI is GitHub Actions.

## 1. Workflow Topology

```text
all.yml (entry: push to main, pull_request, manual)
└── all_configure.yml     # change detection → runtime pipeline config
    └── all_cicd.yml
        ├── all_ci.yml    # quality gates (fan-out below)
        ├── all_cd.yml    # deployment (placeholder until a target exists)
        └── boilerplate-tests.yml   # template repo only (§6)
            ⇣
        "Workflow end marker"       # the single required status check
```

- `all_ci.yml` fans out to three parallel jobs: **docs-checker** (markdown style + link validation over `docs/`), **sast** (OpenGrep), and **ci-backend** (`_ci_dotnet.yml`, §3).
- The chain ends in one aggregate job, **"Workflow end marker"**, which succeeds only if everything upstream did. Branch protection requires exactly this one check.

**Why one required check:** required-check lists in branch settings rot as jobs are added/renamed; a single marker job makes "everything green" the invariant and keeps governance config stable.

## 2. Change Detection

`all_configure.yml` computes what actually changed (backend paths, docs, workflows) and emits a JSON config consumed downstream — a docs-only PR skips the backend gate cluster entirely. It also runs a path case-sensitivity check (macOS/Linux filesystem mismatches).

**Why:** fast pipelines keep the under-5-minute budget ([§10 of the toolchain doc](06-build-toolchain-and-quality-gates.md#10-build-performance-budget)); skipping is safe because skipped jobs can't be required — the marker aggregates only what ran.

## 3. Backend CI Contract (`_ci_dotnet.yml`)

Jobs, in dependency order:

| Job | Runs | Gate |
|---|---|---|
| `compile` | locked-mode restore, `csharpier check`, build with all analyzers, destructive-migration guard | zero warnings, zero format drift, NuGetAudit high/critical = error |
| `tests` | architecture → unit → integration test projects; coverage collection | ReportGenerator threshold gate (95/80 total, 95 Domain; 100 template) + PR coverage comment |
| `mutation-tests` | Stryker.NET on Domain (`--test-runner mtp`) | `thresholds.break` (95; 100 template) |
| `dependency-security-check` | on PRs touching the dependency graph: audit of newly added packages | fail on high/critical advisories |
| `compose-health-and-startup` | build the container image, assert `HEALTHCHECK` present and passing, boot against compose dependencies until the startup marker | image health + startup smoke |

Later jobs reuse the compile job's artifacts instead of rebuilding; gates already passed are skipped downstream (`compile` proves formatting once, `tests` doesn't re-check it). The job set is intentionally equivalent to local `./validate` — CI confirms, it should never be the first place a failure appears.

## 4. Scheduled Automation

- **Renovate** (dependency updates): weekday-morning schedule; groups non-major updates; **minimum release age 5 days** (supply-chain caution against hijacked releases); patch updates auto-merge, minor/major need review; understands `Directory.Packages.props`, `dotnet-tools.json`, lock files, compose image digests, and GitHub Action pins.
- **Daily full vulnerability audit** of all dependencies (not just changed ones), so advisories published after merge still page someone.

## 5. Supply-Chain Pinning

- GitHub Actions are pinned by **commit SHA**, never by tag (tags are mutable).
- Container base images and compose service images are pinned by **digest**.
- Package restore runs in locked mode against committed lock files ([toolchain §4](06-build-toolchain-and-quality-gates.md#4-dependency-integrity-and-vulnerability-gates)).

**Enforced by:** OpenGrep rules (unpinned action/image = blocking finding) + locked-mode restore.

## 6. Template-Only Regression Protection

The template repository runs an extra workflow that aggregates test/coverage/mutation report digests and diffs them against the base branch — a silent regression in the test suite itself (deleted tests, weakened assertions producing fewer reports) fails the template's CI. The workflow is gated by repository name; client projects **remove it** and keep everything else.

## 7. Branch Governance

`.github/default_branch_ruleset.json` (applied at bootstrap) protects `main` and release branches:

- Pull requests required; **≥ 1 approval**; stale approvals dismissed on new commits; all review threads resolved.
- Code-owner review where a `CODEOWNERS` file exists.
- **Linear history**; merge methods limited to **squash/rebase**.
- Required status check: `Workflow end marker`. No force pushes, no deletions.

## 8. Required Secrets and Variables

| Name | Used by |
|---|---|
| `RENOVATE_TOKEN` | Renovate workflow (PR creation) |
| deployment credentials | `all_cd.yml` once a deploy target exists |

Everything else (database, JWT, management credentials) is **runtime environment configuration**, not CI secrets — see [Bootstrap Checklist](04-project-bootstrap-checklist.md).

## 9. Collaboration Artifacts

- PR template: intent, linked issue, checklist mirroring the [pre-PR self-review](02-architecture-overview.md#81-fast-self-review-before-opening-a-pr).
- One concern per PR; action-based titles; behavior changes ship with tests and doc updates in the same PR.

## 10. Do / Do Not

✅ Do: fix the cause when CI is red — the gates are the product's immune system.
✅ Do: keep local `./validate` and the CI job set equivalent when adding a gate.
❌ Do not: weaken a guard (skip a job, drop a threshold, unpin an action) to get a merge through — that's an architecture discussion, not a workaround.
❌ Do not: run the template-only digest workflow in client projects.
❌ Do not: merge dependency-update PRs without review just because they're green (patch automerge is the only sanctioned exception).

## Navigation

⬅️ Previous: [Security, Observability and Error Handling](09-security-observability-and-error-handling.md) · ➡️ Next: [Feature Matrix](11-feature-matrix.md)
