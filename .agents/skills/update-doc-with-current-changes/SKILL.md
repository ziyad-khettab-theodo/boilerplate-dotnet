---
name: update-doc-with-current-changes
description: Update existing docs from real code deltas versus `origin/main` (or a user-provided base ref). Focus on truth, precision, and concise incremental updates.
---

# Update Doc With Current Changes

Primary goal: update existing documentation from changes since `origin/main` (or an input base ref), not from memory.

## Inputs

- `base_ref`: default `origin/main`; allow override (for example `origin/release/x`).
- `docs_root`: default root `README.md` as discovery entrypoint; derive `doc_targets` from docs linked there unless user says otherwise.
- `include_runtime_examples`: default `false`.

## Workflow

1. Resolve baseline.
- Fetch latest `base_ref` from remote before analysis.
- Ensure `base_ref` exists locally after fetch.
- Ensure current branch is on top of `base_ref` (`git merge-base --is-ancestor <base_ref> HEAD`).
- If not on top, stop and request rebase/merge on latest `base_ref` first (to reduce doc merge conflicts).
- Build delta from `git diff <base_ref>...HEAD` plus uncommitted changes.

2. Build doc-impact map.
- Read changed code/config/tests only.
- Also include non-obvious guardrails linked to the delta (CI workflows, scripts, build/test harness, security config).
- Start doc exploration at root `README.md`; build `doc_targets` from linked docs as primary map for update destinations.
- Map each relevant change to an existing doc section/file.
- Update existing docs first; create new doc file only if no existing file fits.

3. Write from source truth.
- Every technical claim must be verifiable in current source.
- If a claim says “enforced”, name the exact rule/test/plugin and the exact constraint.
- Use real code examples from this repo; if impossible, mark example as illustrative.
- Do not rephrase unchanged text. Edit only when meaning changes, drift exists, or clarity gaps block understanding.
- Do not reference docs outside `doc_targets` unless user explicitly asks.

4. Run 3 short refinement passes.
- Pass 1: coverage (`changed behavior/config -> doc update`, including non-obvious guardrails).
- Pass 2: truthness (paths, commands, URLs, thresholds, auth/rules; every referenced path must exist).
- Pass 3: concision/coherence (remove ambiguity, keep terminology/links/order coherent, avoid style-only churn).

5. Final quality checks.
- No stale references, TODO/TBD placeholders, or contradictory statements.
- No stale evidence paths (renamed/moved files, packages, workflows).
- Summarize what changed and what was intentionally left unchanged.

## Command Patterns

```bash
git fetch --prune origin <base_ref>
git merge-base --is-ancestor <base_ref> HEAD
git diff --name-status <base_ref>...HEAD
git diff --name-only <base_ref>...HEAD
git status --porcelain
rg -n "TODO|TBD|FIXME" <doc_targets>
rg -n "<keyword>" <doc_targets>
test -e <path_from_docs>
```

## Output Contract

- Prefer incremental edits over rewrites.
- Keep diff minimal: prefer line edits over section rewrites.
- Keep content concise and diff-driven.
- Do not change text only to rephrase.
- Include path-level evidence for non-obvious claims.
- If the delta has no doc impact, keep docs unchanged and report that explicitly.
