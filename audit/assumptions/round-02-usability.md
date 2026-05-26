# Phase 1 — Round 02 — Usability Discovery

## Repo Snapshot

- Round 01 referenced "13 packages"; the solution actually ships **19 src projects** (`Pulse.slnx` enumerates them). The README's "Projects" list omits `CosmosDb`, `MongoDB`, `MySql`, and `Redis` packages entirely, even though their csproj files are present and packable.
- All packable projects override `<RootNamespace>NetEvolve.Pulse</RootNamespace>` — every type from every package collapses into the same namespace, which affects IntelliSense discoverability and may produce ambiguous extension methods.
- Public extension-method verbs split inconsistently: persistence uses `Add{Provider}Outbox`, transports use `Use{Provider}Transport`, but `MongoDB`/`CosmosDb` use `Use{Provider}Outbox` and `SQLite` exposes BOTH `AddSQLiteOutbox` AND `UseSQLiteOutbox`.
- No `CHANGELOG.md`, no `samples/` directory, no `.template.config` despite the README narrative implying a "Quick Start" template.

## Assumptions

U16: Extension-method verb (`Add*Outbox` vs `Use*Outbox` vs `Use*Transport`) is not consistent across packages and is undocumented.
   Evidence: `src/NetEvolve.Pulse.SQLite/SQLiteExtensions.cs:44` (`AddSQLiteOutbox`) and `src/NetEvolve.Pulse.SQLite/SQLiteExtensions.cs:222` (`UseSQLiteOutbox`); `src/NetEvolve.Pulse.MongoDB/MongoDbExtensions.cs:92` (`UseMongoDbOutbox`); `src/NetEvolve.Pulse.CosmosDb/CosmosDbExtensions.cs:96` (`UseCosmosDbOutbox`); `src/NetEvolve.Pulse.Kafka/KafkaExtensions.cs:44` (`UseKafkaTransport`); `src/NetEvolve.Pulse.SqlServer/SqlServerExtensions.cs:44` (`AddSqlServerOutbox`). No ADR in `decisions/` defines the verb pattern.
   Why it matters: A new user cannot predict the registration verb from the package name; copying a snippet from SQLite into a SqlServer setup will compile-fail or behave unexpectedly. The mixed exposure on a single provider (SQLite) actively confuses about intent (which to call?).
   Test idea: Grep all public extension methods on `IMediatorBuilder` whose target type contains the substring of the package name, group by verb prefix, expect a single canonical verb per concept; ask three .NET devs to register a SqlServer outbox + a Kafka transport from package names alone — measure success rate.

U17: PULSE003 diagnostic is implemented but absent from the source-generator README's diagnostics table.
   Evidence: `src/NetEvolve.Pulse.SourceGeneration/DiagnosticDescriptors.cs:37-44` (`MissingPulseHandlerAttribute` PULSE003, `Info`); `src/NetEvolve.Pulse.SourceGeneration/README.md:148-156` lists PULSE001/002/004/005/006 only. Top-level `README.md:21` and `README.md:67` advertise "PULSE001–PULSE006 diagnostics" but the package README is the only place users discover causes/fixes.
   Why it matters: Users seeing an unfamiliar `PULSE003` warning in their build log have no canonical reference to look it up; the marketing copy ("PULSE001–PULSE006") makes the omission look like a typo bug.
   Test idea: `grep -c PULSE003 src/NetEvolve.Pulse.SourceGeneration/README.md` returns 0; `grep -c PULSE003 src/NetEvolve.Pulse.SourceGeneration/DiagnosticDescriptors.cs` returns ≥ 1.

U18: No `DiagnosticDescriptor` in the source generator sets a `helpLinkUri`, so IDE "Show error help" navigates nowhere.
   Evidence: `src/NetEvolve.Pulse.SourceGeneration/DiagnosticDescriptors.cs:13-83` — all six descriptors are constructed with positional/named args ending at `isEnabledByDefault`; `helpLinkUri` is never supplied. `Grep helpLinkUri src/` returns zero matches.
   Why it matters: Diagnostic IDs are most useful when they link to a docs URL. Without `helpLinkUri`, "Show potential fixes" / Ctrl+F1 in VS yields a generic Microsoft Learn page (or 404 to `learn.microsoft.com/dotnet/csharp/misc/PULSE004`). Compare to Roslyn's own analyzers (CA*, IDE*) which all set the link.
   Test idea: For each public descriptor, assert `descriptor.HelpLinkUri` is non-empty; alternatively, build a sample project that violates each rule and verify the IDE light-bulb's "Get help on this error" opens a working URL.

U19: `OutboxProcessorOptions` and `OutboxOptions` cannot be bound from `IConfiguration` (`appsettings.json`); no `BindConfiguration` overload exists.
   Evidence: `src/NetEvolve.Pulse/OutboxExtensions.cs:39-76` — `AddOutbox` only accepts `Action<OutboxOptions>` and `Action<OutboxProcessorOptions>`. No `services.Configure<OutboxProcessorOptions>(config.GetSection("Pulse:Outbox"))` overload, and no per-provider `Add*Outbox(IConfiguration)` variant. `Grep "IConfiguration\b" src/` only finds them inside XML-doc samples that show how a *caller* would resolve a connection string, not Pulse binding.
   Why it matters: The 12-Factor convention (and ASP.NET Core idiom) is to bind options from configuration. Forcing `Action<TOptions>` means production deployments cannot tweak `BatchSize` / `PollingInterval` / `MaxRetryCount` without recompilation; environment-specific overrides require boilerplate in `Program.cs`.
   Test idea: Try `services.AddPulse(c => c.AddOutbox()).Configure<OutboxProcessorOptions>(builder.Configuration.GetSection("Outbox"));` and observe that nothing reads the section automatically — but no first-class fluent API exposes this.

U20: Telemetry tags do not follow OpenTelemetry semantic conventions (`messaging.*`, `db.*`, `code.*`).
   Evidence: `src/NetEvolve.Pulse/Internals/Defaults.cs:67-134` — all tag names are bespoke under the `pulse.*` prefix (`pulse.event.name`, `pulse.request.correlation_id`, `pulse.exception.stacktrace`, etc.). README boasts "OpenTelemetry-friendly hooks through `AddActivityAndMetrics()`" (`README.md:61`), but `Grep "messaging\.system|db\.system" src/` returns zero matches. Outbox transports (Kafka/RabbitMQ/AzureServiceBus/Dapr) also do not emit `messaging.system=kafka` etc.
   Why it matters: OTel back-ends (Tempo, Honeycomb, Azure Monitor, AWS X-Ray) auto-classify spans by `messaging.system` / `db.system` etc. Custom `pulse.*` tags require per-deployment dashboards and breaks distributed-tracing correlation with the rest of the OTel ecosystem. The README's "OpenTelemetry-friendly" claim is partly aspirational.
   Test idea: Boot a sample app with `AddActivityAndMetrics()`, capture spans via `OpenTelemetry.Exporter.InMemory`, and assert that command/query spans include `messaging.operation`/`code.function` per the OTel semantic-conventions spec.

U21: There is no `Microsoft.Extensions.Diagnostics.HealthChecks` integration for the outbox processor or transports despite each transport exposing `IsHealthyAsync`.
   Evidence: `src/NetEvolve.Pulse.Extensibility/Outbox/IMessageTransport.cs:105` references HealthChecks only in a doc comment; `Grep "IHealthCheck|AddHealthChecks" src/` returns one comment hit, zero implementation hits. Kafka README (`src/NetEvolve.Pulse.Kafka/README.md:44`) says "`IsHealthyAsync` queries cluster metadata; returns `false` when the broker is unreachable" — but the README never shows how to surface that on `/healthz`.
   Why it matters: Production .NET 8+ services expect health-probe wiring out of the box for hosted services. Users must hand-roll an `IHealthCheck` that resolves `IMessageTransport` and calls `IsHealthyAsync`. A pre-built `services.AddHealthChecks().AddPulseOutbox()` would be expected idiomatic API.
   Test idea: Search the NuGet feed for `NetEvolve.Pulse.HealthChecks` package — not present. In a sample app, wire `MapHealthChecks("/healthz")` and observe that nothing reports on outbox queue depth or transport connectivity.

U22: `CosmosDb` package ships without a `README.md`, so the package on NuGet would render the default placeholder.
   Evidence: `Glob src/NetEvolve.Pulse.CosmosDb/README.md` → no file. Every other package directory in `src/` has one.
   Why it matters: NuGet's package detail page uses `<PackageReadmeFile>` (when set) or falls back to description. Top-level `Directory.Build.props:5` sets `PackageProjectUrl` but does NOT set `PackageReadmeFile`. Combined with the missing file, CosmosDb consumers see the longest, least-readable surface. Other packages have READMEs but the prop is still unset everywhere, so even the present READMEs may not be packed.
   Test idea: Run `dotnet pack src/NetEvolve.Pulse.CosmosDb` and inspect the `.nupkg` — the README is not embedded; for CosmosDb it doesn't even exist on disk to embed.

U23: Per-package README depth varies by an order of magnitude — Kafka (44 lines) and MySql (59 lines) are dramatically shallower than peer transports/providers.
   Evidence: Line counts: `Kafka/README.md` 44, `MySql/README.md` 59, `AzureServiceBus/README.md` 86, `Redis/README.md` 87, vs `SqlServer/README.md` 446, `Polly/README.md` 348, `EntityFramework/README.md` 344, `Dapr/README.md` 343. `Kafka/README.md` lacks any options-class reference, sample app, telemetry section, or troubleshooting guide.
   Why it matters: A user picking between transports based on README quality will perceive Kafka as a second-class citizen. Knowledge that exists for SqlServer (transactional scope, schema scripts, idempotency) is silently absent from MySql's doc even though `MySqlExtensions.cs` exposes equivalent APIs.
   Test idea: For each provider package, define a checklist (Quick Start, Options, Schema/Setup, Troubleshooting, Telemetry, Migration); compute coverage matrix; flag any package below 60% completion.

U24: Every packable project overrides `<RootNamespace>NetEvolve.Pulse</RootNamespace>`, collapsing 16 packages into a single namespace.
   Evidence: `Grep -l RootNamespace src/*/*.csproj` returns 16 matches; spot checks (`AspNetCore`, `AzureServiceBus`, `Redis`, `CosmosDb`) all set `<RootNamespace>NetEvolve.Pulse</RootNamespace>`. The .NET design guideline is one namespace per assembly with package suffix.
   Why it matters: `using NetEvolve.Pulse;` after installing 8 packages dumps every public type from all of them into the current scope, including dozens of unused extension methods. IntelliSense becomes noisy; reflection-based discovery cannot filter by namespace; documentation generators (DocFX) cannot scope by package. Conventional patterns (Microsoft.Extensions.*, Polly.*, MassTransit.*) keep types in matching namespaces.
   Test idea: After installing only `NetEvolve.Pulse.Kafka`, type `NetEvolve.Pulse.` in an IDE; count completions — expect dozens unrelated to Kafka. Compare with MassTransit, where `MassTransit.Kafka.*` is isolated.

U25: `IRequest<TResponse>` XML doc on line 10 has a structural error that breaks doc rendering.
   Evidence: `src/NetEvolve.Pulse.Extensibility/IRequest.cs:10` — `</typeparam>/// <remarks>` is a single line with no newline between the closing tag and the next doc comment. SandCastle/DocFX and Visual Studio QuickInfo render this as a malformed remarks block.
   Why it matters: `IRequest<T>` is the most-derived-from base contract in the public surface area; broken hover documentation is the first impression for new users using Ctrl+K, Ctrl+I.
   Test idea: Open `IRequest.cs` in Visual Studio, hover the type — observe truncated/empty remarks; build with `-p:GenerateDocumentationFile=true` (currently unset in `Directory.Build.props`) and check for `CS1570` "XML comment has badly formed XML".

U26: No project sets `<GenerateDocumentationFile>true</GenerateDocumentationFile>`, so consumers do not get XML IntelliSense for any Pulse package.
   Evidence: `Grep GenerateDocumentationFile src/ Directory.Build.props Directory.Packages.props Directory.Solution.props` → 0 matches. No `.editorconfig` warning configuration for CS1591 either. Yet the codebase contains extensive `<summary>`, `<remarks>`, `<example>` markup on public types (e.g., `OutboxExtensions.cs`, `PollyExtensions.cs`, every interface in `Extensibility/`).
   Why it matters: Hours of authored XML doc never reach end users via NuGet — they see only signatures, not the curated examples. This is a wasted-investment problem: enabling the property would surface all of it.
   Test idea: Run `dotnet build -p:GenerateDocumentationFile=true` mentally on `NetEvolve.Pulse.csproj`; expect dozens of CS1591 (missing-doc) warnings to surface, indicating coverage gaps the build currently does not enforce.

U27: No `CHANGELOG.md` exists at the repo root despite GitVersion + Conventional Commits being mandated by ADR.
   Evidence: `Glob **/CHANGELOG*` → no files. `GitVersion.yml:1-5` configures trunk-based preview1 versioning. `decisions/2025-07-10-conventional-commits.md` mandates Conventional Commits. `Directory.Build.props:6` sets `<PackageReleaseNotes>$(PackageProjectUrl)/releases</PackageReleaseNotes>` — meaning users land on GitHub Releases for release notes, but the repository itself doesn't track them.
   Why it matters: Consumers (and humans) typically read CHANGELOG.md to grasp upgrade impact; pointing only to GitHub Releases makes offline workflows (corporate forks, NuGet feed mirrors) lose change context. The mature .NET libraries (Polly, MassTransit, Serilog) all maintain CHANGELOG.md.
   Test idea: Inspect a published package on NuGet — `PackageReleaseNotes` will resolve to the URL string literal, not embedded notes; check whether `[NuGet]/dailydevops/pulse` shows useful release notes vs. just a link.

U28: `CONTRIBUTING.md` does not mention Docker / Testcontainers prerequisites for integration tests.
   Evidence: `CONTRIBUTING.md:23-29` describes running `dotnet test` from solution root with no mention of Docker. `tests/NetEvolve.Pulse.Tests.Integration/` contains `SqlServerContainerFixture.cs`, `PostgreSqlContainerFixture.cs`, `MySqlContainerFixture.cs`, `RedisContainerFixture.cs` — all Testcontainers-based and require a Docker daemon.
   Why it matters: New contributors run `dotnet test` per the guide, half the integration suite hangs/fails on test discovery (`DockerNotFound` / `Docker.DotNet.DockerApiException`), and there is no documented troubleshooting path. The `Prerequisites` section of `README.md:77-82` also omits Docker.
   Test idea: On a clean machine without Docker, follow `CONTRIBUTING.md` verbatim; observe `dotnet test` failure modes; verify they are not surfaced anywhere as a known prerequisite.

U29: No `.template.config` / template package despite README naming a `templates/` folder; the existing folder only contains markdown templates, not `dotnet new` templates.
   Evidence: `Glob templates/**` → `templates/Predefined.cs`, `templates/adr.md`, `templates/readme-project.md`, `templates/readme-solution.md`. No `.template.config/template.json`. Root `README.md:192` shows `templates/           # Documentation templates` confirming intent — but the directory's name strongly implies a `dotnet new pulse` experience that does not exist.
   Why it matters: Developers searching `dotnet new --list | grep -i pulse` find nothing; the README's quick-start (`README.md:115-136`) is the only onboarding path. Competing frameworks (MediatR/Worker templates, MassTransit templates) ship `dotnet new` packs to bootstrap full samples.
   Test idea: Run `dotnet new --search pulse` — expect no result. Compare against `dotnet new --search masstransit`.

U30: Multiple public `*Options` classes lack any `IValidateOptions<T>` validator, allowing nonsensical configurations (negative `BatchSize`, zero `PollingInterval`, etc.).
   Evidence: `OutboxProcessorOptions` (`src/NetEvolve.Pulse/Outbox/OutboxProcessorOptions.cs:10`), `OutboxOptions`, `AzureServiceBusTransportOptions`, `RabbitMqTransportOptions`, `MongoDbOutboxOptions`, `CosmosDbOutboxOptions`, `DaprMessageTransportOptions`, `TimeoutRequestInterceptorOptions`, `QueryCachingOptions`, `IdempotencyKeyOptions` — none are referenced by a corresponding `IValidateOptions<T>` (only `LoggingInterceptorOptionsValidator.cs` exists). `AddOutbox` calls `services.AddOptions<OutboxProcessorOptions>()` without `.Validate(...)` or `.ValidateOnStart()` (`OutboxExtensions.cs:57`).
   Why it matters: Setting `BatchSize = 0` silently halts processing; setting `PollingInterval = TimeSpan.Zero` busy-loops the hosted service consuming CPU. Failures emerge in production, not at startup. The Options pattern's built-in safety net (`.ValidateOnStart()`) is bypassed.
   Test idea: Configure `OutboxProcessorOptions` with `BatchSize = -1` and `PollingInterval = TimeSpan.Zero`; start the host; expect no validation exception; observe the hosted service either does nothing or spins.

U31: Public exception messages are hardcoded English string literals — no resource files for localization.
   Evidence: Hardcoded throws include `src/NetEvolve.Pulse/Dispatchers/ParallelEventDispatcher.cs:79` (`"One or more event handlers failed."`), `src/NetEvolve.Pulse.EntityFramework/ModelBuilderExtensions.cs:85` (`$"Unsupported EF Core provider: {providerName}"`), `src/NetEvolve.Pulse.SqlServer/Outbox/SqlServerOutboxRepository.cs:136` (`"Transaction has no associated connection."`). No `.resx` files in `src/`.
   Why it matters: Multilingual operations teams (common in EU / APAC enterprises) cannot localize error messages reaching logs/UI. The decision `decisions/2025-07-11-english-as-project-language.md` mandates English source, but does not preclude resource-based localization. As-is, the choice is implicit and uncommunicated to package consumers.
   Test idea: Search for `.resx` under `src/` — expect zero. Check whether any public exception type derives from a class with localization hooks — none do.

U32: Source generator README (`src/NetEvolve.Pulse.SourceGeneration/README.md:161,166`) references a `NetEvolve.Pulse.Attributes` package as a "Related Package" / prerequisite, but no such package exists.
   Evidence: `Pulse.slnx:26-44` lists no `NetEvolve.Pulse.Attributes` project; `Directory.Packages.props` has no central version for it; the actual `[PulseHandler]` attribute lives in `src/NetEvolve.Pulse.Extensibility/Attributes/`. The source-gen README lines 161 ("`NetEvolve.Pulse.Attributes` package for the `[PulseHandler]` attribute") and 166 (link to `https://www.nuget.org/packages/NetEvolve.Pulse.Attributes/`) point at a phantom package.
   Why it matters: Following the README leads users to `dotnet add package NetEvolve.Pulse.Attributes` → 404 from NuGet, dead-end install experience. Either the package is planned (then the doc is premature) or the doc is wrong (then it must point at `NetEvolve.Pulse.Extensibility`).
   Test idea: `curl -I https://www.nuget.org/packages/NetEvolve.Pulse.Attributes/` returns 404 (or the redirect to a "not found" page).
