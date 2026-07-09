# C# Language and Async Conventions

The stack-native reference for **how C# is written here**: casing, type idioms, the async rulebook, and dependency injection. These are decisions already made — follow them so neither a developer nor an AI agent has to re-derive them per file.

- This is the **reference**. The step-by-step *teaching* version is the [Hands-On Build Guide §2.0](12-hands-on-build-guide.md#20-the-c-youll-meet-in-this-phase).
- Coming from the JVM? The Java→C# translation lives in the [Guide for Java/Spring Developers](13-guide-for-java-spring-developers.md).
- Placement and type-name taxonomy (folders, `*UseCase`, `I*Port`, …) live in [Project Structure and Conventions](03-project-structure-and-conventions.md); this doc covers language idioms.

## 1. Casing

Public members are PascalCase **regardless of visibility** — including private methods (`private void Validate()`, never `validate()`). Only parameters, locals, and private fields are camelCase.

| Element | Case | Example |
|---|---|---|
| Method (any visibility) | PascalCase | `IsNullOrWhiteSpace`, `Handle`, `Validate` |
| Property | PascalCase | `Value`, `Username` |
| Class / record / struct / enum | PascalCase | `Username`, `GetUsersUseCase` |
| Namespace | PascalCase | `Theodo.DotnetBoilerplate.Common.Domain` |
| Public field / constant | PascalCase | `MaxLength` |
| Private field | `_camelCase` (leading underscore) | `_users` |
| Parameter / local variable | camelCase | `cancellationToken`, `users` |

## 2. Types and immutability

- **`record` for data; `class` for behavior/identity-owned-by-a-framework.** Domain entities, value objects, commands, queries, events, and endpoint request/response types are **`record`** with `required`/`init` members (value equality + non-destructive `with` for free). See [Architecture Overview](02-architecture-overview.md).
- **`sealed` by default**, unless a type is explicitly designed for inheritance (abstract test bases, extension points). `static` classes are implicitly sealed.
- **`internal`** for infrastructure not part of a domain contract; `public` for the contract surface.
- **`required` + `init`**: "must be provided, can never change." The default shape for domain records — no half-built objects, no post-construction mutation.
- **Properties vs fields**: expose data as a **property**; keep internal storage in a **private `_field`**. Property forms: `{ get; }` (set once in constructor — use when you validate), `{ get; init; }` (set via initializer/`with` — the domain default), `{ get; set; }` (mutable — EF `*DbEntity` only), `=> expr` (computed, no storage).
- **`Guid` over `string`** for identifiers unless serialization or an external protocol requires `string`.
- **Immutable collections** (`ImmutableList<T>`, `ImmutableHashSet<T>`, … from `System.Collections.Immutable`) in domain fields and non-private signatures. Received collections can't be mutated by the receiver; returned ones can't be mutated by the caller.
- **Persistence boundary exemption**: `*DbEntity` types are **mutable `class`es** with `{ get; set; }` and may use `List<T>`/arrays — EF's change tracker requires mutability and identity. The exemption stops at the adapter; the domain never sees a `*DbEntity`. See [Data Persistence](08-data-persistence-and-migrations.md).
- **Expression body (`=>`) vs block body**: use an **expression body** when the whole member is a single expression (one-line methods, pass-throughs, factories, mappers, computed properties); use a **block body** the moment there's more than one statement, a local, a `try/catch`, an early return, or branching. Never contort code into one expression to keep the `=>` — readability wins over brevity, so reach for a block and a local when the expression stops being easy to read. (These are *expression-bodied members*, distinct from lambdas, which reuse the same `=>` for anonymous delegates.)

## 3. Async and concurrency

The one-line model: **`Task<T>` in a signature is the async *contract* (present whenever I/O is downstream); `async` is an *implementation detail*; `await` *consumes* a Task without blocking a thread.** These are three separate things — don't conflate them.

### 3.1 What returns `Task`

- **I/O-bound operations return `Task<T>` (or `ValueTask<T>`) — async all the way.** Ports, use cases that call ports, and endpoints are `Task`-typed because there is I/O somewhere down the chain. Blocking a thread to avoid this caps throughput and risks deadlock.
- **Purely CPU-bound / no-I/O code is synchronous** — return the plain `T`, no `Task`, no `CancellationToken`. `Task` is about *waiting on the outside world*, not about being a use case. Genuinely heavy CPU work belongs in a background job (`202 Accepted` + status), not a fake-async `Task.Run` on the request thread.

### 3.2 `async` is an implementation detail

**Never put `async` on an interface (port) signature.** The interface declares a `Task`-returning method; whether an implementation uses `await` is its own business.

```csharp
public interface IUserRepositoryPort { Task<ImmutableList<User>> FindAll(CancellationToken cancellationToken); }
```

**Pure pass-through: return the Task directly, no `async`/`await`.** Add `async` only when the body must `await` and then do more work (transform, combine, branch, wrap in `try`).

```csharp
// pass-through — no async needed
public Task<ImmutableList<User>> Handle(GetUsersQuery q, CancellationToken ct) => _repo.FindAll(ct);
// awaits + maps — async required
public async Task<ImmutableList<User>> FindAll(CancellationToken ct) =>
    (await _db.Users.ToListAsync(ct)).Select(r => r.ToDomain()).ToImmutableList();
```

**Elision caveat**: only return-the-task-directly when there is **no** `using`/`try` around the call — otherwise the resource disposes / exception escapes before the task completes. Inside a `using`/`try`, you must `await`.

### 3.3 `CancellationToken`

- **Thread it through every async layer** (endpoint → use case → port → adapter) and pass it to framework I/O calls (`ToListAsync(cancellationToken)`).
- **Always name the parameter `cancellationToken`** (not `ct`).
- **Three sources**: the framework-supplied parameter (production), `CancellationToken.None` (unit tests — "never cancels"), `TestContext.Current.CancellationToken` (integration tests).
- Omit it only from **pure synchronous** domain functions that do no I/O (nothing to cancel; a long CPU loop may still accept one and call `ThrowIfCancellationRequested()`).

### 3.4 Banned / dangerous

- **Never block on a task in a request path**: no `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()`. They freeze a thread and can deadlock; `.Result`/`.Wait()` also wrap errors in `AggregateException`. Use `await`. (The only safe blocking read is after the task is already known-complete, e.g. right after `await Task.WhenAll(...)`.)
- **Never `async void`** except UI/framework event handlers. Its exceptions can't be caught by the caller and can crash the process. Fire-and-forget belongs in a `BackgroundService`/hosted service or a `Channel`, never `async void`.

### 3.5 Concurrency and exceptions

- **Independent awaits run concurrently** via `Task.WhenAll`; sequential only when one result feeds the next.

  ```csharp
  var a = portA.Load(ct); var b = portB.Load(ct);   // start both
  await Task.WhenAll(a, b);                          // await together
  var (ra, rb) = (await a, await b);
  ```

- **Exceptions propagate through `await` naturally** — a `try/catch` around an `await` catches the awaited operation's exception as if synchronous. This is why the domain throws typed exceptions and the boundary catches them (see [error handling](09-security-observability-and-error-handling.md#7-error-contract)).
- **`Task.WhenAll` aggregation**: if several tasks fail, `await` rethrows the **first**; all failures live on the task's `.Exception` (`AggregateException`). Inspect `.Exception` (or each task) to log them all.

### 3.6 `ValueTask`

Default to `Task<T>`. Prefer `ValueTask<T>` only on **hot paths that usually complete synchronously** (e.g. cache hits, `IExceptionHandler.TryHandleAsync`, `IAsyncLifetime`) where avoiding the `Task` allocation matters. Never `await` a `ValueTask` more than once.

## 4. Dependency injection

- **Constructor (primary-constructor) injection only.** No property/field injection; no service-locator (`IServiceProvider`) resolution in business code.
- **Lifetimes — pick by state and dependencies**:
  - `AddScoped` — one instance per HTTP request. Use cases, and **anything that touches the database** (a `DbContext`-backed adapter *must* be scoped).
  - `AddSingleton` — one for the app's lifetime. Only for stateless, thread-safe services (e.g. the in-memory adapter, a clock).
  - `AddTransient` — a fresh instance each resolution. Rarely needed.
- A `Scoped` service must never be captured by a `Singleton` (captive dependency) — it silently shares one instance across requests.

## Navigation

⬅️ Related: [Architecture Overview](02-architecture-overview.md) · [Project Structure and Conventions](03-project-structure-and-conventions.md) · [Guide for Java/Spring Developers](13-guide-for-java-spring-developers.md)
