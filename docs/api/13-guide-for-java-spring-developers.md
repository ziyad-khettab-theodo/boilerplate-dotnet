# Guide for Java/Spring Developers

This project is a faithful port of Theodo's Spring Boot boilerplate: same architecture, same conventions, same rigor — expressed through .NET-native mechanisms. This is the **only** document that references the Java stack; everything else in this guide stands alone. Read this if you're arriving from the JVM: it maps your existing mental model onto this codebase and explains every place where the port deliberately diverges.

## 1. Terminology Dictionary

| Spring / Java | Here | Notes |
|---|---|---|
| Maven / `pom.xml` | MSBuild / `.csproj` + `Directory.Build.props` | shared props ≈ parent POM, without inheritance quirks |
| Maven wrapper pin | `global.json` | pins the SDK per repo |
| dependency `<dependencyManagement>` / BOM | `Directory.Packages.props` (Central Package Management) | one reviewed version list |
| `package` | `namespace` (PascalCase, matches folders) | `features.users.domain` → `Features.Users` in the `Domain` project |
| Spring bean / `@Component` | a DI **registration** in code | no classpath scanning by default — `services.AddScoped<…>()` |
| `@Autowired` constructor injection | constructor injection (primary constructors) | same idea, no annotation needed |
| `@Configuration` class | extension methods over `IServiceCollection` | `AddSecurityPipeline(…)` etc., called from `Program.cs` |
| `application.yml` + profiles | `appsettings.json` + `appsettings.<Environment>.json` | environment from `ASPNETCORE_ENVIRONMENT` |
| `@ConfigurationProperties` + `@Validated` | options pattern + `ValidateDataAnnotations().ValidateOnStart()` | same fail-fast contract |
| `@RestController` + `@PostMapping` | endpoint class implementing `IEndpoint` + `MapPost` | see §3.2 |
| Jakarta Bean Validation | DataAnnotations + built-in minimal-API validation | attribute style is near-identical |
| `@PreAuthorize` | `.RequireAuthorization(policy)` + deny-by-default fallback | see §3.2 |
| `@RestControllerAdvice` | `IExceptionHandler` chain | ordered, map-only handlers |
| Spring Data JPA / Hibernate | EF Core (`DbContext`) | `DbContext` ≈ `EntityManager` + repository in one |
| Liquibase changelogs | EF Core Migrations | diff-based generation ≈ `liquibase:diff`, snapshot instead of reference DB |
| `@Scheduled` | hosted services under `Features/<subdomain>/Schedules/` | |
| Spring events / `@EventListener` | `IEventPublisherPort` + listener services | same pattern, explicit port |
| Actuator | health checks + management endpoints (`/managementz`) | same "never anonymous" rule |
| springdoc / Swagger UI | built-in OpenAPI generation + Scalar UI | code-first either way |
| Logback + JSON encoder | `ILogger` + JSON console formatter + OTel | no third-party logging framework needed |
| JUnit 5 + AssertJ | xUnit v3 + AwesomeAssertions | `assertThat(x).isEqualTo(y)` → `x.Should().Be(y)` |
| Mockito | fakes-first policy (as before); NSubstitute if a mock is truly needed | |
| Testcontainers | Testcontainers for .NET | same project, same idea |
| MockMvc / `@SpringBootTest` | `WebApplicationFactory<Program>` | in-memory host, real pipeline |
| ArchUnit | ArchUnitNET | same fluent API family |
| JaCoCo / PIT | coverage collector + ReportGenerator gate / Stryker.NET | same thresholds |
| Spotless | CSharpier | same auto-format-on-commit flow |
| Checker Framework `@Nullable` | nullable reference types (`T?`) | built into the language — see §4 |
| Lombok / builders (Jilt) | `record` + `required` + `init` + `with` | the language absorbed the tooling — see §4 |
| Eclipse Collections immutables | `System.Collections.Immutable` | same immutable-by-default policy |
| OWASP dependency-check | NuGetAudit (built into restore) | high/critical = build error |
| Maven Enforcer convergence | CPM + `packages.lock.json` + locked restore | a real lockfile |

## 2. What Was Kept Verbatim

The architecture is unchanged: hexagonal layers with the same dependency direction; one-operation `*UseCase` classes with a single `Handle`; `I*Port` interfaces implemented by centralized adapters; REPR endpoints with endpoint-local request/response records; the `/public/` path convention; stable `errors.*` ProblemDetails codes; the test taxonomy (`*UnitTests` / `*IntegrationTests` / `*ApplicationTests` / `*ContractTests`), fakes-over-mocks, contract tests, query-count guardrails, the Act pattern; the 95/80/95/98 thresholds with the template-100% profile; the doc system itself. If you knew the Spring boilerplate, every rule you remember still holds — only the enforcement mechanism may differ.

Also deliberately unchanged: **use cases live in the domain** — there is no separate "Application layer", even though four-layer Clean Architecture layouts are common in .NET. The reference's two-layer feature (domain + api, infra shared) is simpler and loses nothing; the port keeps it.

## 3. Structure and Endpoint Style

### 3.1 Single Maven module → single .NET project (faithful)

The reference is a single Maven module, organized **feature-first**: `features/<subdomain>/{domain,api}`, infrastructure centralized in `common/infra`, every layer boundary enforced by ArchUnit. The .NET port keeps that shape exactly — **one application project**, `Features/<subdomain>/{Domain,Api}` folders, infrastructure centralized in `Common/Infra`, layer boundaries enforced by **ArchUnitNET**. Namespaces map 1:1: `Theodo.DotnetBoilerplate.Features.Users.Domain.UseCases.Signup` ≡ `com.theodo.springboilerplate.features.users.domain.usecases.signup`.

Why not split each layer into its own .NET project — a common .NET habit that would make "domain touches no framework" a compile error? Because that would invert the reference's structure: the layer would become the top-level axis, you'd navigate by layer instead of by feature, and a single feature would smear across projects. The architecture is defined by the dependency *rules*, not by how many assemblies enforce them — so the port keeps the reference's feature-first cohesion and lets ArchUnitNET hold the layers apart, exactly as ArchUnit does on the JVM. (An architecture test that fails the build is the same enforcement strength the reference already trusts.)

The one structural thing .NET *does* force: **tests are separate projects** — there is no `src/test` convention. So `ArchitectureTests`, `UnitTests`, `IntegrationTests`, and `TestHelpers` are sibling projects that reference the single application project.

### 3.2 Controllers → minimal API endpoint classes

Spring MVC *wants* multi-action controllers; the original boilerplate uses ArchUnit rules (no class-level `@RequestMapping`, one handler per class) to force it into REPR. ASP.NET Core has an MVC controller stack too — and the port doesn't use it. Minimal APIs with one `IEndpoint` class per route **are** REPR natively: the framework's grain matches the rule, so there's nothing to fight.

Authorization got strictly stronger in the move: `@PreAuthorize`-on-every-method is an *annotate-or-fail-the-test* model; here a deny-by-default fallback policy secures anything that forgets to declare, **and** the explicit-declaration rule is still enforced by an architecture test. Forgetting now produces a 401, not an exposure.

## 4. Where C# Absorbed the Tooling

- **Null safety:** the Checker Framework's non-null-by-default model is a compiler feature here (`<Nullable>enable</Nullable>`). `@Nullable` → `T?`; `@EnsuresNonNullIf` → `[MemberNotNullWhen]`; `castNonNull` → the `!` operator (same last-resort discipline). No stub files, no annotation processor.
- **Builders and immutability:** the Lombok-ban/staged-builder policy existed to prevent constructing objects with missing non-null fields. C# `record` + `required` + `init` gives that guarantee natively — a partially-initialized record is a compile error. Test fixtures use `with` expressions instead of generated builders.
- **`Optional<T>`:** doesn't exist. A nullable return (`User?`) plus compiler flow analysis is the idiom — the compiler forces the "empty" branch the way `Optional` politely suggested it.

## 5. Gotchas for Java Habits

| Habit | Here |
|---|---|
| `getX()` / `setX()` | properties: `user.Username`, `{ get; init; }` — never write accessor methods |
| Streams: `list.stream().filter(…).collect(…)` | LINQ: `list.Where(…).Select(…).ToImmutableList()` — no `.stream()`, no terminal-op ceremony |
| Checked exceptions | don't exist; all exceptions are unchecked. The discipline comes from typed `DomainException` subclasses + handler tests |
| Blocking calls are normal | I/O is `async`/`await` all the way: signatures return `Task<T>`, and blocking (`.Result`, `.Wait()`) is a deadlock-and-starvation bug, not a style choice |
| `equals`/`hashCode` (or Lombok) | records give structural equality for free; classes compare by reference |
| camelCase methods, lowercase packages | **PascalCase** for types, methods, properties, namespaces; camelCase only for locals/parameters |
| Field injection sneaks in | constructor (primary constructor) injection only — same rule as the original, but field injection isn't even idiomatic here |
| `mvn verify` reflex | `./validate` — same muscle, same meaning |
| String-heavy config keys | options records bound once, validated at startup; business code never touches `IConfiguration` |
| Runtime classpath surprises | there is no classpath: assembly references are compile-time, and a missing package is a build error, not a `ClassNotFoundException` |

### 5.1 Casing Reference

This table is the Java→C# contrast. The stack-native rules (casing plus type/async/DI idioms, with no JVM framing) are the canonical [C# Language and Async Conventions](14-csharp-language-and-async-conventions.md) — cite that from code, this from a JVM mindset.

The single biggest surface-level shift from Java: **methods and properties are PascalCase regardless of visibility** (`private void Validate()`, not `validate()`). Only locals, parameters, and private fields are camelCase.

| Element | Java | C# | Example |
|---|---|---|---|
| Method (any visibility) | camelCase | **PascalCase** | `IsNullOrWhiteSpace`, `Handle` |
| Property (vs Java getter) | `getValue()` | **PascalCase** | `Value`, `Username` |
| Class / record / struct / enum | PascalCase | PascalCase | `Username` |
| Namespace (vs package) | lowercase | **PascalCase** | `Common.Domain` |
| Public field / constant | camelCase / UPPER | **PascalCase** | `MaxLength` |
| Private field | camelCase | **`_camelCase`** | `_value` |
| Parameter / local variable | camelCase | camelCase | `value` |

## 6. Reading Order for JVM Arrivals

1. This document, then the [Architecture Overview](02-architecture-overview.md) — you'll recognize every rule.
2. [Nullability](07-nullability-and-nullable-reference-types.md) — the biggest day-one language shift.
3. [Testing Platform](05-testing-platform.md) — taxonomy is familiar; the fixtures/idioms are new.
4. Build something via the [Hands-On Build Guide](12-hands-on-build-guide.md) — the fastest way to make the toolchain muscle memory.

## Navigation

⬅️ Previous: [Hands-On Build Guide](12-hands-on-build-guide.md) · ➡️ Back to: [Developer Guide](README.md)
