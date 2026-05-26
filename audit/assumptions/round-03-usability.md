# Phase 1 — Round 03 — Usability Discovery

## Repo Snapshot

- 19 production packages under `src/` (the Pulse.slnx lists all 19 including CosmosDb, MongoDB, MySql, Redis), but the root README's "Projects" and "Project Structure" sections only enumerate 15 — CosmosDb/MongoDB/MySql/Redis are silently missing from the index.
- Modern `.slnx` solution format only; no fallback `.sln` shipped. `global.json` does not pin an SDK version, only `Microsoft.Testing.Platform` as the runner.
- `Directory.Solution.props` is a 6-line file that sets a single MSBuild property — and `Directory.Build.props` ships no `GenerateDocumentationFile`, no `PackageReleaseNotes` per package (static URL only), and no `PackageVersion`/lockstep enforcement across the 19 packages.
- Telemetry surface is hardcoded in `internal static class Defaults` (`"NetEvolve.Pulse"` for both `ActivitySource` and `Meter`) — no public constant for OTel consumers to bind to, no documented `AddSource`/`AddMeter` snippet anywhere in any README.
- A real `IOutboxManagement` API exists for dead-letter inspection/replay but is referenced in zero README files — operator runbook missing.

## Assumptions

U33: The OpenTelemetry meter name and activity source name are hardcoded in an `internal` class with no public constant.
   Evidence: src/NetEvolve.Pulse/Internals/Defaults.cs:16 (`new ActivitySource("NetEvolve.Pulse", Version)`), src/NetEvolve.Pulse/Internals/Defaults.cs:31 (`new Meter("NetEvolve.Pulse", Version)`), src/NetEvolve.Pulse/Internals/Defaults.cs:9 (`internal static class Defaults`).
   Why it matters: Consumers wiring OTel pipelines (`AddSource(...)`, `AddMeter(...)`) must hand-copy the magic string `"NetEvolve.Pulse"` and hope it doesn't change between releases. A typo silently drops all telemetry. No `public const string MeterName` / `ActivitySourceName` is exposed anywhere.
   Test idea: Grep the public surface for a string constant containing the meter/activity source name; assert presence. Compile a consumer that imports the constant — expected to fail until exposed.

U34: Neither the root README nor `src/NetEvolve.Pulse/README.md` contains the canonical OTel "Configure" snippet (`tracerProviderBuilder.AddSource("NetEvolve.Pulse")` / `meterProviderBuilder.AddMeter("NetEvolve.Pulse")`).
   Evidence: README.md:60-61 mentions `AddActivityAndMetrics()` only as a registration call; README.md:141 says "Align logging and tracing setup with your OpenTelemetry configuration" with zero code. src/NetEvolve.Pulse/README.md:18 and :300 mention OTel but never show `AddSource`/`AddMeter`. No occurrences of `AddSource(` or `AddMeter(` anywhere under `src/**/README.md` or root README.
   Why it matters: Following the README, a developer calls `AddActivityAndMetrics()`, configures OTel, sees no traces or metrics arrive, and has no way to know they need to subscribe by name. This is the single most common OTel onboarding failure.
   Test idea: Run the README "Quick Use" snippet plus a standard OTel exporter; assert zero spans/metrics until `AddSource("NetEvolve.Pulse")` is added.

U35: The dead-letter inspection/replay API (`IOutboxManagement`) exists but is undocumented; no README mentions `IOutboxManagement`, `GetDeadLetterMessagesAsync`, `ReplayMessageAsync`, or `ReplayAllDeadLetterAsync`.
   Evidence: src/NetEvolve.Pulse.Extensibility/Outbox/IOutboxManagement.cs:19-83 defines a fully fleshed admin API. `grep -rn "IOutboxManagement\|ReplayMessage\|GetDeadLetter"` across `**/*.md` returns only 3 files (`src/NetEvolve.Pulse.SqlServer/README.md`, `PostgreSql`, `MySql`) and only because they reference the dead-letter status enum value; none show the API in use.
   Why it matters: An operator hitting a dead-letter situation in production has no documented runbook — they have to read source to discover `IOutboxManagement` exists, where to resolve it from DI, and what the page-size semantics are. The README of `NetEvolve.Pulse` (the package that ships the processor) is silent.
   Test idea: Search every package README for the substring `IOutboxManagement`. Expect at least one mention with a `services.GetRequiredService<IOutboxManagement>()` example.

U36: No CHANGELOG.md exists and `PackageReleaseNotes` is a static URL pointing at the GitHub releases page — release notes per package version cannot be rendered by NuGet.org.
   Evidence: `grep -l CHANGELOG` returns no files. Directory.Build.props:6 — `<PackageReleaseNotes>$(PackageProjectUrl)/releases</PackageReleaseNotes>`. Release-drafter only writes a single global `$CHANGES` block (`.github/release-drafter.yml:5-7`).
   Why it matters: Consumers using NuGet's "Show Release Notes" feature see a URL instead of actual notes. Each of the 19 packages gets the same generic URL, so a SqlServer-only bugfix forces consumers to scrape the global release log to find what changed in their package.
   Test idea: Pack any project; inspect `.nupkg`'s `.nuspec` for `<releaseNotes>` — expect a URL, not human-readable text.

U37: 19 production packages share one global `_ProjectTargetFrameworks` and (per `Directory.Build.props`) one set of `PackageTags`/`CopyrightYearStart`; there is no documented versioning policy (lockstep vs. independent) and `GitVersion.yml` operates at the repo level only.
   Evidence: Directory.Build.props:7 (`<PackageTags>$(PackageTags);cqrs;mediator;</PackageTags>`), Directory.Build.props:15 (`_ProjectTargetFrameworks` shared). GitVersion.yml:1-5 — single `ManualDeployment` config for the whole repo. `grep -l Lockstep` returns nothing.
   Why it matters: A consumer pinning `NetEvolve.Pulse.Kafka 2.3.0` and `NetEvolve.Pulse.AzureServiceBus 2.2.0` cannot tell whether mixing minor versions is supported. No README sentence states "all packages ship lockstep" or "transports may be picked independently".
   Test idea: Search the repo for the words "lockstep", "version compatibility", "version matrix" — expect zero hits. Diff the published versions on NuGet for the 19 packages — confirm they ship in lockstep, then assert the docs say so.

U38: No `dotnet user-secrets` / `IConfiguration` binding example is shown for transports that require secrets — Azure Service Bus connection strings, Kafka SASL credentials, and RabbitMQ passwords appear as hardcoded literals in READMEs.
   Evidence: src/NetEvolve.Pulse.RabbitMQ/README.md contains `Password = "guest"` inside a `ConnectionFactory` snippet. src/NetEvolve.Pulse.AzureServiceBus/README.md:42 uses `builder.Configuration["ServiceBus:ConnectionString"]` but never points readers at `dotnet user-secrets set ServiceBus:ConnectionString ...` or Key Vault. `grep -l "user-secrets\|UserSecretsId"` returns no files in the repo.
   Why it matters: Copy-paste from the README puts hardcoded passwords into git. No package csproj sets `<UserSecretsId>` and no README points at the secret-management best practice — a documented onboarding hazard.
   Test idea: For each transport README, assert at least one mention of `dotnet user-secrets` or `Azure Key Vault` near the connection-string sample.

U39: `IOutboxRepository.IsHealthyAsync` exists but there is no `Microsoft.Extensions.Diagnostics.HealthChecks` integration; no `AddHealthChecks().AddPulseOutbox()` (or similar) extension is shipped.
   Evidence: `grep -l "IHealthCheck\|AddHealthChecks"` under `src` returns zero matches. src/NetEvolve.Pulse.Extensibility/Outbox/IOutboxRepository.cs:201 only exposes `IsHealthyAsync` for internal processor use. README.md:0-243 makes no mention of `HealthChecks` integration.
   Why it matters: Operators expect `/healthz` to surface outbox queue depth and transport reachability through the standard `IHealthCheck` mechanism. With nothing shipped, every consumer has to wrap `IsHealthyAsync` themselves and likely re-invents the same wheel.
   Test idea: Search for any `using Microsoft.Extensions.Diagnostics.HealthChecks` in `src/`; expected to be absent.

U40: Public types accidentally live in a `*.Internals` namespace, suggesting reviewer/IDE confusion about what is intended to be public API.
   Evidence: src/NetEvolve.Pulse.RabbitMQ/Internals/IRabbitMqChannelAdapter.cs:1 (`namespace NetEvolve.Pulse.Internals;`) — the type itself is `internal interface`, but the *namespace* is `Internals`. Same for `RabbitMqChannelAdapter.cs`, `IRabbitMqConnectionAdapter.cs`, `RabbitMqConnectionAdapter.cs`, and `src/NetEvolve.Pulse/Internals/Defaults.cs`. `Defaults.ActivitySource` and `Defaults.Meter` are `public static` (Defaults.cs:24, :39) but the enclosing class is `internal` — discoverable inside the assembly only.
   Why it matters: `*.Internals` namespace prefix is a convention for "do not depend on this". The mix of `public` members inside an `internal` class plus the namespace name make the boundary muddy — refactors that flip `internal` → `public` would unexpectedly expose `Defaults` and the version-derived meter/activity-source name as part of the public API contract.
   Test idea: Add a unit test that enumerates types in the `NetEvolve.Pulse.Internals` namespace and asserts they are all `internal`; fail if any become `public` accidentally.

U41: No sample/example project ships in the repo; `templates/` contains only README/ADR templates, not runnable samples for any of the 19 packages.
   Evidence: `ls templates/` shows only `Predefined.cs`, `adr.md`, `readme-project.md`, `readme-solution.md`. No `samples/` or `examples/` directory exists at repo root. CONTRIBUTING.md:1-45 never mentions a sample app. README.md does not link any runnable sample.
   Why it matters: A consumer evaluating "does Kafka transport work with batch sending?" must read source. The README "Quick Use" is the only end-to-end snippet, and that snippet is already flagged in U01 as non-compiling. Each of the 4 transport packages would benefit from a 10-line runnable bash sample.
   Test idea: For each integration package, assert that the README's first code block can be turned into a `dotnet new console` + `dotnet run` that doesn't throw `InvalidOperationException` at startup.

U42: The `Microsoft.SourceLink.GitHub` global package is included but `IPayloadSerializer.Deserialize<T>` returns `T?` without `[NotNullIfNotNull]` annotation, leaving nullability flow analysis broken for callers.
   Evidence: src/NetEvolve.Pulse.Extensibility/IPayloadSerializer.cs:74,85 — `T? Deserialize<T>(string payload);` / `T? Deserialize<T>(byte[] payload);`. No `[NotNullIfNotNull(nameof(payload))]` despite the doc comment saying "or `null` if the payload represents a null value". `grep -l "NotNullWhen\|MaybeNullWhen"` in `src/` returns zero matches.
   Why it matters: Callers passing a non-null payload still get a nullable result, forcing defensive `??` everywhere. The whole `*.Extensibility` package has zero `[NotNull*When]` attributes — public-API nullable polish is missing.
   Test idea: For each public method returning `T?`/`string?` whose nullness depends on inputs, assert presence of an attribute from `System.Diagnostics.CodeAnalysis`. Expect zero hits today.

U43: Renovate is configured (`renovate.json`) but Dependabot is also configured to watch only `devcontainers` — duplicated update mechanism leaves a stale-dependabot trap.
   Evidence: .github/dependabot.yml:11-22 — single update target `devcontainers`, daily schedule, but `renovate.json:13` lists `enabledManagers: [custom.regex, dockerfile, github-actions, nuget]` — Renovate handles dockerfiles, including devcontainer Dockerfiles via the `dockerfile` manager. Both tools may fight over Docker base images in `.devcontainer/Dockerfile`.
   Why it matters: Two competing bots opening overlapping PRs is a known operational anti-pattern. Either remove `dependabot.yml` or move all updaters to Dependabot. The current state is undocumented.
   Test idea: Trigger both bots in a fork; assert exactly one source-of-truth opens PRs for devcontainer image bumps.

U44: The `NetEvolve.Pulse` main-package README (the one rendered on NuGet.org) never mentions `[PulseHandler]` / `[PulseHandler<T>]` despite source-gen being the recommended registration path; consumers landing on NuGet first won't discover the source-gen flow.
   Evidence: `grep "PulseHandler\|AddGeneratedPulseHandlers" src/NetEvolve.Pulse/README.md` returns zero matches. The attribute is defined in `src/NetEvolve.Pulse.Extensibility/Attributes/PulseHandlerAttribute.cs:39` and described only in the `SourceGeneration` package README and the root README.
   Why it matters: NuGet.org renders the per-package README. A consumer who installs `NetEvolve.Pulse` reads its README, copies the "Quick Use" with `services.AddScoped<ICommandHandler<...>>(...)` manual registrations, and never learns that source generation exists. The README must cross-reference the source-gen attribute or recommend installing the source-gen package.
   Test idea: Render `src/NetEvolve.Pulse/README.md` as HTML; assert at least one anchor containing the substring `PulseHandler`.

U45: `CA2007` (ConfigureAwait) is suppressed per-class on every database outbox repository / management file (14 files) via `[SuppressMessage]` instead of via a project-wide `.editorconfig` rule — and the suppression Justification claims `ConfigureAwait` IS applied to all awaits, which contradicts the suppression's purpose.
   Evidence: src/NetEvolve.Pulse.SqlServer/Outbox/SqlServerOutboxRepository.cs:22-26, plus 13 sibling files in `SQLite`, `PostgreSql`, `MySql`, `CosmosDb`. Justification text: "await using statements in library code; ConfigureAwait applied to all Task-returning awaits." — but if `ConfigureAwait` is applied, CA2007 would not fire and the suppression would be unnecessary.
   Why it matters: 14 copies of the same `[SuppressMessage]` block is a maintenance hazard; any new outbox provider author must remember to add it. The contradiction in the Justification suggests the suppression hides a real bug — `await using` statements do not capture context but CA2007 still flags them in older analyzer versions, and the suppression masks any genuine missed-`ConfigureAwait` elsewhere in the file.
   Test idea: Remove one `[SuppressMessage("Reliability","CA2007")]` from a single repository; build with TreatWarningsAsErrors and observe whether CA2007 fires legitimately on a non-`await using` statement.
