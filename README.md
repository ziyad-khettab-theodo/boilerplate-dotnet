# boilerplate-dotnet

An opinionated .NET 10 REST API boilerplate: hexagonal architecture with compile-time and test-enforced boundaries, merge-blocking quality gates (formatting, zero warnings, null safety, coverage, mutation, vulnerability audit), a ProblemDetails error contract, and OpenTelemetry observability.

The platform is currently **documentation-first**: the full engineering system is specified in the docs and is being built by hand from the build guide. The [Feature Matrix](docs/api/11-feature-matrix.md) tracks what exists.

## Documentation

- **[Developer Guide](docs/api/README.md)** — architecture, conventions, testing platform, quality gates, CI contracts.
- **[Hands-On Build Guide](docs/api/12-hands-on-build-guide.md)** — constructing the platform file by file.
- **[Guide for Java/Spring Developers](docs/api/13-guide-for-java-spring-developers.md)** — arriving from the JVM.
