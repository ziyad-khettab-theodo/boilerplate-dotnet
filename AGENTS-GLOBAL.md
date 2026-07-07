## General Instructions
- When reporting command execution in commentary or final response, prefix the command itself with `[exitcode=<n> ✅]` or `[exitcode=<n> ❌]`. Do this every time you mention a command result, including partial runs and retries.
- When the user challenges or requests a correction and the reason is missing, unclear, or ambiguous from the prompt or prior context, ask for clarification before any further action.
- Before asserting library, framework, JDK, or dependency behavior that affects implementation or spec decisions, verify against primary source: project dependency version, official docs, source, bytecode, or local tests. State when verified; if not verified, label as assumption.
- Before any edit, list applicable AGENTS rules (3-6 bullets). Blocker.
- Before final response, run AGENTS compliance self-audit on changed code/tests. If any violation exists, fix first; do not report completion before fix.
- If a blocker rule is ambiguous, ask the user before coding; do not assume.

## Instruction-Editing Rules
- When editing any `AGENTS*.md`, replace or move superseded rules.
- Classify each rule as `stack-agnostic` or `repo-specific` before editing.
- Put `stack-agnostic` rules in `AGENTS-GLOBAL.md` and `repo-specific` rules in repo `AGENTS.md`.
- After editing, check both files for duplicate or weaker overlapping wording and remove it.

## Change-Control Gate (Blocker)
- Default mode after spec/discussion/Q&A, failure report, CI/test/job error report, or failed-run link: `READ-ONLY`.
- In `READ-ONLY`: no file edits, no `apply_patch`, no write commands, no `git add/commit`, no `git push`, no PR updates/comments, no CI reruns.
- In `READ-ONLY`: allow inspection only (`rg`, `cat`, `sed`, `nl`, `git diff`, `git show`, logs/status lookup, docs lookup).
- Exit `READ-ONLY` for file edits only on explicit user authorization to change files.
- Treat imperative change orders as authorization, including `APPLY`, `IMPLEMENT`, `FIX`, `UPDATE`, `CHANGE`, `MAKE THE CHANGE`, `GO`, `PROCEED`, `DO IT`, `YES DO IT`, or clear equivalents.
- Ambiguous approval or discussion responses like `ok`, `sounds good`, `thanks`, or `continue` do not authorize edits unless they clearly refer to a pending edit request.
- Never run `git add`, `git commit`, `git commit --amend`, `git push`, branch creation/switching, pull request creation/update/comment, or CI rerun unless the user explicitly requests that exact action in the current turn.
- If edits create mixed staged/unstaged changes, leave the index unchanged and report the mixed state.
- If user intent ambiguous (discussion vs implementation): ask one clarifying question; do not edit first.
- Before first edit, present: target files + concise planned diff summary; wait for confirmation.
- If any edit occurs without authorization: stop immediately, report, offer revert.

## Responsibility Boundaries
- Before adding logic, identify which existing component already owns the responsibility.
- Prefer extending the input to that component over duplicating part of its behavior elsewhere.
- When an existing component already performs a responsibility such as recursion, traversal, filtering, caching, parsing, or normalization, do not reimplement that behavior in the caller. Provide the missing root, input, configuration, or context to that component instead.
- When fixing a gap, first ask: "Can the existing abstraction handle this if I give it one more root/input/context?" If yes, do that.
- After the change, the diff should make the responsibility split clearer, not introduce a second place that knows the same rules.

## Change Scope
- Keep existing behavior unchanged unless the change requires it. Make the smallest possible change at the boundary.
- Prefer minimal diffs; avoid unnecessary refactors or extra tests.
- Avoid adding code/fields early; keep each commit minimal to immediate needs.
- Avoid "while I'm here" improvements such as filtering, reshaping, or generalizing data unless required by the failing case.
- When following an existing pattern, verify each copied step applies in the new context. Do not add work that the existing command/tool already performs, especially if it increases runtime, downloads, coupling, maintenance, or failure surface.

## General Coding Instructions
- Avoid extra blank lines unless formatter introduces them.
- Run required build/test command before each commit or amend.
- Commit names: outcome-focused, plain language, user benefit; avoid internal jargon.
- Refactors behavior-preserving only; if semantics shift, treat as breaking and redesign.
- Use intent-revealing names that match actual responsibility and scope; avoid generic, overconstrained, or misleading names, and include domain/condition context when relevant.
  e.g. :
  - `isEqualsInvocation(Method method, Object... args)` instead of `isEquals(Object left, Object right)`
  - `isAnnotationNameIn(AnnotationExpr annotation, ImmutableSet<String> names)` instead of `isNamed(AnnotationExpr annotation, ImmutableSet<String> names)`
  - `findAncestorNodeOfType(Node node, Class<T> type)` instead of `findAncestor(Node node, Class<T> type)`
  - `TraceCorrelationConfiguration` instead of `ContextPropagationConfiguration` for configuration wiring trace context propagation + trace correlation headers
- Generic type parameters: start names with `T` and use meaningful names over single letters, for example `TFieldName` instead of `F`.
- Spec-like method flow; step-by-step intent first.
- Pipeline separation (blocker): forbid mixed control-and-action logic in the same unit unless trivially linear (<=10 lines, single branch, single side effect). Example: replace a unit that resolves baseline + chooses mode + runs scanner with `resolveBaseline()`, `buildScanPlan()`, `runScan(plan)`.
- After any refactor, re-check all pending changes against AGENTS.md rules; repeat until no rule-driven adjustments remain.
- After each change, verify affected methods are still used and keep minimal necessary visibility.
- Avoid warning suppressions, but when necessary, keep them as narrow in scope as possible without excessive redundancy.
- Comment only hidden constraints or surprising framework/API behavior.
- Explain why the code exists and what breaks without it.
- Put the comment next to the non-obvious line; keep it one precise sentence.
- After each code change, run the impacted test class.
- Name variables by value held, not usage.
- Prefer single-use helper extraction in long methods when it removes single-use locals and shortens call site.
- Be thorough; explore simplification paths for readability.
- Convert static helpers that use instance state into instance methods to avoid parameter threading.
- Prefer filter/map pipelines over loop+if when it simplifies flow
- Prefer keeping files under 250 lines and functions under 25 lines.
- Prefer returning new result collections over mutating caller-owned containers in non-private methods.
- Avoid new helper methods that only wrap a single expression; inline locally unless the expression is hard to read or reused 3+ times.
- Create local variables to keep calls on one line; avoid wrapped arguments.
- Prefer early returns to flatten flow, unless they just guard a single trailing statement; then keep a simple if guard.
- Prefer switch over chained conditionals for discrete-value branching when switch supports the type.
- Apply YAGNI to helpers when readability neutral: if a helper is single‑use and intent is clear at the call site, inline and remove the helper.
- Readability overrides YAGNI; keep helper if flow is clearer, even single‑use.
- Method naming: verb‑leading by default.
- Getter naming: allow bare x() only for pure direct-value getters (field/constant return only: no compute, lookup, filter, derivation, waiting, retry, parsing, or side effects).
- Getter naming: allow getX() for retrieval/lookup accessors, including null-check/validation and fail-fast behavior (for example `orElseThrow`).
- Methods that do computation/lookup/selection and are not getX() accessors must use verb-leading names that state behavior (for example find/select/resolve/collect/build), not bare getter-style names.
- Factory/helper methods that construct objects must use verb-leading names such as build/create/new, except established factory conventions such as from/of.
- Allow concise non-verb helper names only when they match familiar library idioms and remain immediately clear at the call site (for example minBy.../maxBy...).
- Prefer result-returning APIs to avoid repeating work/errors; e.g., parse once and carry ParseResult for errors + AST.
- Always add missing tests to reproduce failures when a user challenges the implementation.
- Compute derived data at point of use when source inputs already available; avoid early arg derivation and propagation.
- Inline constants or locals used once, unless a project convention requires extraction.

## Failure Types
- Types representing failure, rejection, denial, or unsuccessful outcomes must expose a stable reason enum, unless the type name already represents exactly one stable reason.
- Define the reason enum inside the type when the reason values are owned only by that type.
- Include the reason in diagnostic output such as `toString()` when that output is used for logs, assertions, events, or troubleshooting.

## Global Tests Instructions
- Test features over implementation details; remove redundant unit tests if needed.
- Domain business logic used by a use case must be tested through the use case public contract, not direct tests of internal domain plumbing.
- Direct unit tests are allowed for value-object invariants that cannot be reached through a use case because invalid values cannot be constructed.
- Avoid production-logic reuse in tests; independent oracles to catch regressions.
- Use parameterized tests when multiple inputs exercise the same behavior with the same assertion shape.
- Do not use parameterized tests when each case needs different setup, different assertions, or a distinct behavior name for readability.
- Prefer parameterized tests for validation boundaries: valid limits, invalid adjacent values, and representative invalid values from each rejected range.
- Extract long asserted expressions into locals for readability.
- Act block: explicit SUT call, one call only; move // Act to real call.
- Assertions: start from Act result; extract SUT call into variable when assertion would hide it.
- Comments: no Given/When/Then or Arrange/Assert; // Act only.
- Spacing: always blank line after SUT call; blank line before // Act unless there's no prior instruction/comment.
- In every test method, call the SUT directly in the Act step; never call a helper that hides or wraps the SUT.
- Default to merged assertion chains; collapse post‑Act processing into a single assertion chain.
- Avoid tests that use non-public surface or reflection; cover via public contracts.
- Use test-owned fixtures for cross-cutting behavior tests; choose the narrowest fixture that expresses the behavior, and avoid unrelated production endpoints or infrastructure unless they are part of the behavior under test.
- Use helper assertions only when they are narrow and composable (predicate/extractor style).
- Keep main behavior expectations visible at call site.
- API contract tests: keep expected response JSON inline at the assertion site; exact payload is behavior under test.
- Format expected response JSON text blocks as readable JSON, matching nearby payload assertions; prefer local clarity over reducing vertical space.
- Avoid non-explicit positional placeholders in test literals (for example \%s) when explicit values are clearer; explicit named placeholders (for example \${myvar}) are allowed.
- Avoid accidental test sample values that obscure constraints; choose distinct, constraint-revealing values for related fields.
