# Nullability and Nullable Reference Types

Nullability bugs are frequent and expensive: they surface far from their cause, in production, as `NullReferenceException`. This project eliminates them at compile time — **nullability diagnostics fail the build** (`TreatWarningsAsErrors`), making null-safety a quality gate, not a habit.

## 1. The Model: Non-Null by Default

> **Concept primer.** With `<Nullable>enable</Nullable>` (set repo-wide in `Directory.Build.props`), every reference type in a signature is a **contract**: `User` means *never null*; `User?` means *may be null, and the compiler forces you to check before use*. The compiler tracks null-state through the whole method (flow analysis) and raises diagnostics on every violation.

- Parameters, return values, properties, and fields are **non-null unless annotated** with `?`.
- Generic type arguments carry nullability too: `List<string?>` is a list that may contain nulls; `List<string>?` is a list that may itself be null.
- The annotation is the API documentation: a `User? FindByUsername(Username username)` tells every caller "absence is a normal outcome here".

## 2. Practical Rules

- **Annotate absence honestly.** If a method can return "not found", the return type says so (`User?`). If a parameter is optional, it is `T?` — never a non-null type that's "sometimes null in practice".
- **Initialize non-null members at construction.** Use `required` properties on records/classes so the compiler forces every construction site to provide a value:

  ```csharp
  public record User
  {
      public required Guid Id { get; init; }
      public required Username Username { get; init; }
  }
  ```

- **Don't null-check non-null parameters.** Inside this codebase the contract is compiler-enforced; defensive `if (x is null) throw …` on a non-null parameter is dead code that hides real signals. (Boundary inputs — HTTP payloads, external APIs — are different: they are validated, and arrive as nullable until validated.)
- **Teach the compiler through flow attributes** when a helper establishes null-state:

  ```csharp
  public class TokenValidator
  {
      [MemberNotNullWhen(true, nameof(Claims))]
      public bool IsValid { get; }
      public TokenClaims? Claims { get; }
  }
  // after: if (validator.IsValid) { validator.Claims.Subject … }  — no diagnostic
  ```

  Likewise `[NotNullWhen(true)]` for `bool TryGet(... , out T? value)` patterns, and `[return: NotNullIfNotNull(nameof(arg))]` for pass-through helpers.

## 3. The Null-Forgiving Operator `!` — Last Resort Only

`value!` tells the compiler "trust me, this is not null" — it silences the diagnostic **without any runtime check**. Discipline:

1. First try to **refine the contract**: better types, `required`, flow attributes, pattern matching.
2. If impossible (a framework API the compiler can't see through), use `!` with a one-line comment stating the invariant that guarantees non-null.
3. A `!` without a justifying comment is a review blocker.

**Why:** every unjustified `!` is a suppressed future `NullReferenceException` with the stack trace pointing somewhere else.

## 4. Common Error Patterns and Fixes

| Diagnostic | Meaning | Fix |
|---|---|---|
| `CS8618` — non-nullable property is uninitialized | a member could be observed null after construction | mark it `required`, initialize it in the constructor, or make it `T?` if absence is legal |
| `CS8602` — dereference of a possibly null reference | using `x.Member` while `x` may be null | pattern-match first: `if (x is not null)` / `x?.Member` / handle the null branch explicitly |
| `CS8603` / `CS8604` — possible null return / argument | passing or returning `T?` where `T` is required | handle the null case before the call, or change the receiving contract to `T?` if null is genuinely valid there |
| `CS8601` — possible null assignment | assigning `T?` into a `T` member | same as above — resolve at the boundary, don't forgive |
| `CS8619`/`CS8620` — nullability mismatch in generics | e.g. `List<string?>` vs `List<string>` | fix the element annotation where the nulls actually enter or leave |

Two recurring shapes worth naming:

- **Lambda capture:** the compiler cannot track null-state into a lambda executed later. Hoist to a local first:

  ```csharp
  if (user.Email is not { } email) return;        // email : non-null local
  recipients.Add(() => Send(email));               // safe inside the lambda
  ```

- **Dictionary/lookup access:** prefer `TryGetValue` (annotated with `[MaybeNullWhen(false)]`) over indexing plus manual checks — the compiler understands the pattern.

## 5. Un-annotated Third-Party APIs

Some packages predate nullable annotations ("null-oblivious" code): the compiler stays silent about them instead of protecting you. Policy:

- Confine oblivious APIs to **adapters**. The adapter validates/normalizes and exposes honestly-annotated types to the domain (which, having no package dependencies, never meets oblivious code).
- At the adapter boundary, treat undocumented reference returns as nullable until proven otherwise.

## 6. Do / Do Not

✅ Do: model absence with `T?` and let the compiler drive the handling.
✅ Do: use `required` aggressively — a type that can't be half-built needs no defensive code.
✅ Do: use flow attributes so validation helpers propagate their guarantees.
❌ Do not: sprinkle `!` to silence diagnostics — each one needs an invariant comment.
❌ Do not: null-check parameters the compiler already guarantees.
❌ Do not: weaken `<Nullable>` for a file or project — the gate is only a gate if it is total.

## Navigation

⬅️ Previous: [Build Toolchain and Quality Gates](06-build-toolchain-and-quality-gates.md) · ➡️ Next: [Data Persistence and Migrations](08-data-persistence-and-migrations.md)
