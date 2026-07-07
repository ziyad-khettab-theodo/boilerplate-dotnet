---
name: draft-pr
description: Manually-invoked helper — run it explicitly (e.g. /draft-pr) to write a PR title and description as text from the branch's diff against the base branch. Text only: it does not push and does not run gh. Do NOT auto-trigger it from phrases like "gh pr", "push", "create the PR", or "provide the PR"; when the user mentions PRs in passing, follow their literal request (which may be to actually push and open the PR).
---

# Draft PR

Primary goal: produce an accurate PR title and description from what actually changed on this branch — not from memory of the session.

## Workflow

1. Determine the real delta against the base branch (default `main`; allow an override the user names).
   - `git log --oneline <base>..HEAD` — the commits this branch adds.
   - `git diff --stat <base>...HEAD` — the files and scope touched.
   - Base every claim in the title/description on this output.
2. Write the **title**: one conventional-commit line — `type(scope): summary` (`feat`/`fix`/`docs`/`chore`/`build`/`ci`/`refactor`/`perf`/`test`/`revert`). Pick the type from the dominant change; add a scope (`api`, `guide`, `ci`, …) when it clarifies.
3. Write the **description** (markdown):
   - `## Summary` — grouped bullets by area of change.
   - `## Verification` — how it was checked (build/run/tests, lint, links).
   - `## Notes` — anything a reviewer should know: deferred work, follow-ups, intentional omissions.
   - End with: `🤖 Generated with [Claude Code](https://claude.com/claude-code)`
4. Do **not** run `git push` or `gh pr create`. Offer them as copy-paste commands for the user to run.
5. Push or create the PR only if the user explicitly says "push" or "create the PR" in the current turn.

## Command Patterns

```bash
git log --oneline main..HEAD
git diff --stat main...HEAD
git status --short
# offer, do not run:
git push -u origin <branch>
gh pr create --base main --head <branch> --title "<title>" --body-file <file>
```
