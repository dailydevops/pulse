# Phase 1 — Round 01 — Usability Discovery

> Read-only assumptions. Each must be confirmed or refuted in Phase 2 with file:line evidence.

## Repo Snapshot
~19 src projects (core, source-gen, ASP.NET Core, FluentValidation, HttpCorrelation, Polly, persistence + transport providers). Multi-targets `net8.0;net9.0;net10.0`. Heavy XML docs and per-project READMEs. C# 14 `extension(...)` member syntax pervasively. Central versioning via `Directory.Packages.props`. NuGet metadata per-csproj. No `PackageIcon`/`PackageReadmeFile` anywhere. `SystemTextJsonPayloadSerializer` default and unconditional.

## Assumptions

### U01 — README "Quick Use" snippet does not compile / does not dispatch
- Claim: Root README snippet declares record/handler types at file scope after top-level statements and never builds the provider or dispatches anything. Compile error guaranteed.
- Evidence: `README.md:118-136`; counter-example `src/NetEvolve.Pulse/README.md:42-72`.
- Why it matters: First-impression sample. Copy-paste = immediate compile failure.
- Test idea: Paste the README:118-136 snippet verbatim into a new console project; build on net8/net9/net10.

### U02 — README mis-describes QueryCachingOptions API
- Claim: README claims `QueryCachingOptions` exposes `JsonSerializerOptions`; the type actually only has `ExpirationMode` and `DefaultExpiry`. Serialization is controlled via `IPayloadSerializer` and `IOptions<JsonSerializerOptions>` globally.
- Evidence: `README.md:59`; `src/NetEvolve.Pulse/README.md:152-162`; type at `src/NetEvolve.Pulse.Extensibility/Caching/QueryCachingOptions.cs:10-59`.
- Why it matters: Doc'd code does not compile.
- Test idea: Compile `src/NetEvolve.Pulse/README.md:155-162` snippet → CS0117.

### U03 — XML doc references non-existent NuGet package
- Claim: `AssemblyScanningExtensions` XML doc recommends `NetEvolve.Pulse.Generators`; actual package is `NetEvolve.Pulse.SourceGeneration`. Typo-squat risk on NuGet.
- Evidence: `src/NetEvolve.Pulse/AssemblyScanningExtensions.cs:15`.
- Why it matters: Users `dotnet add package` something that doesn't exist.
- Test idea: Verify package name vs csproj `<PackageId>`.

### U04 — Default JSON serializer breaks AOT silently
- Claim: `SystemTextJsonPayloadSerializer` uses reflection-mode `JsonSerializer.Serialize<T>` / `Deserialize<T>` with no `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]` annotations. Conflicts with the library's AOT-friendly positioning (`HandlerRegistrationExtensions` advertises AOT compatibility).
- Evidence: `src/NetEvolve.Pulse/Serialization/SystemTextJsonPayloadSerializer.cs:32-44`; AOT claim `src/NetEvolve.Pulse/HandlerRegistrationExtensions.cs:9,15-17`.
- Why it matters: AOT users get runtime failures or silent metadata loss without compile-time warnings.
- Test idea: AOT publish a console app using `AddPulse() + AddQueryCaching()`; verify no IL2026/IL3050 warnings even though reflection happens.

### U05 — Critical options classes have no IValidateOptions
- Claim: `OutboxProcessorOptions` accepts `BatchSize<=0`, `PollingInterval<=TimeSpan.Zero`, `BackoffMultiplier<=0`, `ProcessingTimeout=TimeSpan.Zero`. `AzureServiceBusTransportOptions` validation runs inline at first resolution, not at startup. Only `LoggingInterceptorOptions` has a registered validator.
- Evidence: `src/NetEvolve.Pulse/Outbox/OutboxProcessorOptions.cs:10-99`; `src/NetEvolve.Pulse.AzureServiceBus/AzureServiceBusExtensions.cs:75-86`; only validator: `src/NetEvolve.Pulse/Interceptors/LoggingInterceptorOptionsValidator.cs`.
- Why it matters: Misconfig fails deep inside processor at runtime instead of failing fast at startup. No `ValidateOnStart()` wired up.
- Test idea: Configure `BatchSize=0, PollingInterval=Zero`, build host; verify host starts cleanly.

### U06 — Aggressive retry defaults
- Claim: `MaxRetryCount=3`, `EnableExponentialBackoff=false`, `EnableBatchSending=false`. New user calling `AddOutbox()` gets 3 quick retries with no backoff (gated only by polling interval) → dead-letter on transient failures.
- Evidence: `src/NetEvolve.Pulse/Outbox/OutboxProcessorOptions.cs:28,40,47`.
- Why it matters: Headline reliability feature configured for foot-gun.
- Test idea: Processor with no overrides + transport that fails 3× then succeeds → message dead-lettered.

### U07 — Transport extension methods do not enforce AddOutbox
- Claim: `UseAzureServiceBusTransport`, `UseKafkaTransport`, `UseRabbitMqTransport`, `UseMessageTransport<T>` silently replace any existing `IMessageTransport` but do NOT register `IOutboxRepository` or `OutboxProcessorHostedService`. Users following per-package READMEs may get half-wired systems.
- Evidence: `src/NetEvolve.Pulse.AzureServiceBus/README.md:32-45`; `src/NetEvolve.Pulse.AzureServiceBus/AzureServiceBusExtensions.cs:26-60`; `src/NetEvolve.Pulse.Kafka/KafkaExtensions.cs:44-59`; counter-example `src/NetEvolve.Pulse.SqlServer/SqlServerExtensions.cs:194-204`.
- Why it matters: Missing-service exceptions at publish, or silently no-op outbox.
- Test idea: Take ASB README:32-45 quick-start verbatim, build provider, call `mediator.PublishAsync(...)`; observe DI failure or silent no-op.

### U08 — SQL Server schema script not delivered via PackageReference
- Claim: `OutboxOptions.Schema` defaults to `"pulse"`. SQL Server README says "execute the schema script from `Scripts/OutboxMessage.sql`" — script is shipped via NuGet `content\Scripts` (legacy content mechanism that does not flow to PackageReference consumers).
- Evidence: `src/NetEvolve.Pulse/Outbox/OutboxOptions.cs:18`; `src/NetEvolve.Pulse.SqlServer/NetEvolve.Pulse.SqlServer.csproj:18-19`; `src/NetEvolve.Pulse.SqlServer/SqlServerExtensions.cs:23-25`.
- Why it matters: First publish throws "Invalid object name 'pulse.OutboxMessage'"; documented remediation does not deliver the file.
- Test idea: Add `<PackageReference Include="NetEvolve.Pulse.SqlServer" />`, build, search consumer output for `OutboxMessage.sql` → absent.

### U09 — MapStreamQuery uses raw JsonSerializer, ignoring IPayloadSerializer/options
- Claim: `EndpointRouteBuilderExtensions.MapStreamQuery<TQuery,TResponse>` calls `JsonSerializer.Serialize(item)` / `SerializeToUtf8Bytes(item)` with no options — does not honor configured `JsonSerializerOptions` or `IPayloadSerializer`.
- Evidence: `src/NetEvolve.Pulse.AspNetCore/EndpointRouteBuilderExtensions.cs:226,248`.
- Why it matters: `MapQuery` and `MapStreamQuery` serialize the same DTO differently (camelCase vs PascalCase); blocks AOT for stream endpoints.
- Test idea: Configure camelCase policy; add `MapQuery` and `MapStreamQuery` for same DTO; compare response property casing.

### U10 — KafkaMessageTransport ignores CancellationToken (also Q06)
- Claim: `_producer.Flush(Timeout.InfiniteTimeSpan)` ignores token; `SendBatchAsync` declared with `CancellationToken` but never observes it.
- Evidence: `src/NetEvolve.Pulse.Kafka/Outbox/KafkaMessageTransport.cs:94`; `src/NetEvolve.Pulse/Outbox/OutboxProcessorHostedService.cs:432-435`.
- Why it matters: Stuck broker leaks processor thread; `ProcessingTimeout` does not bound batch sending.
- Test idea: Unreachable broker, send batch, cancel token; assert task completes within `ProcessingTimeout`.

### U11 — Transport extensions silently overwrite previous registration
- Claim: `Use*Transport` methods linear-scan and `Remove()` any existing `IMessageTransport`. No warning if a transport is overwritten by a later call.
- Evidence: `src/NetEvolve.Pulse.AzureServiceBus/AzureServiceBusExtensions.cs:51-58`; `src/NetEvolve.Pulse.Kafka/KafkaExtensions.cs:50-57`; `src/NetEvolve.Pulse.RabbitMQ/RabbitMqExtensions.cs:53-60`; `src/NetEvolve.Pulse/OutboxExtensions.cs:94-101`.
- Why it matters: Silent last-write-wins on foundational service makes misconfig undebuggable.
- Test idea: Register both ASB and Kafka in any order; resolve `IMessageTransport`; assert exactly one is registered and no diagnostic emitted.

### U12 — IMediator registered Scoped, BackgroundService example violates DI lifetime
- Claim: `IMediator` is Scoped, but XML doc example shows `IMediatorSendOnly` injected directly into a `BackgroundService`. With `ValidateScopes=true` (dev default), host startup fails.
- Evidence: `src/NetEvolve.Pulse/ServiceCollectionExtensions.cs:110-111`; example at `src/NetEvolve.Pulse.Extensibility/IMediatorSendOnly.cs:20-34`.
- Why it matters: Copy-paste example throws `InvalidOperationException` at startup.
- Test idea: Implement the example verbatim against `Host.CreateApplicationBuilder()`; observe scope validation failure.

### U13 — NuGet metadata missing icon + embedded README
- Claim: No `PackageIcon` / `PackageReadmeFile` set anywhere. `Directory.Build.props` defines only `Title`, `RepositoryUrl`, `PackageProjectUrl`, `PackageReleaseNotes`, `PackageTags`.
- Evidence: `Directory.Build.props:1-12`; `logo.png` at repo root unused; grep across repo finds no `PackageIcon`/`PackageReadmeFile`.
- Why it matters: NuGet listing shows generic placeholder, no README on package page.
- Test idea: `dotnet pack`; inspect resulting `.nupkg` `.nuspec` for `<icon>`/`<readme>` elements.

### U14 — C# 14 extension blocks lock source build to .NET 10 SDK
- Claim: `extension(TReceiver)` member blocks used across 7+ files; multi-target is `net8.0;net9.0;net10.0` but `LangVersion` is not explicitly set.
- Evidence: `src/NetEvolve.Pulse/AssemblyScanningExtensions.cs:51`; `src/NetEvolve.Pulse.SqlServer/Outbox/SqlServerOutboxOptionsExtensions.cs`; `src/NetEvolve.Pulse.PostgreSql/Outbox/PostgreSqlOutboxOptionsExtensions.cs`; et al.
- Why it matters: Source builds with .NET 8 or .NET 9 SDK fail; CI on older SDKs breaks; cryptic CS1003/CS8400 errors for contributors.
- Test idea: `global.json` pinning to .NET 8 SDK, then `dotnet build` of `Pulse.slnx`.

### U15 — Missing handler gives generic DI exception, no scanning helper
- Claim: `services.AddPulse()` with no further calls and no `[PulseHandler]` source-gen attributes throws a generic `"no service of type ICommandHandler<...>"` at first dispatch. Compared to MediatR's `RegisterServicesFromAssemblyContaining<Program>()` one-liner, Pulse requires source-gen, manual `Add*Handler`, or AOT-incompatible `AddHandlersFromCallingAssembly()`.
- Evidence: `src/NetEvolve.Pulse/ServiceCollectionExtensions.cs:88-114`; error message inherited from `GetRequiredService`.
- Why it matters: #1 first-time error with no actionable remediation in the message.
- Test idea: `services.AddPulse(); …GetRequiredService<IMediator>().SendAsync(new SomeCommand())`; verify error message and that it does not mention scanning or `AddCommandHandler<>`.
