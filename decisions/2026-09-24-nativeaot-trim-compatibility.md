---
authors:
  - Martin Stühmer

applyTo:
  - "src/**/*.cs"
  - "src/**/*.csproj"
  - "samples/NetEvolve.Pulse.Xample.Aot/**"
  - ".github/workflows/aot.yml"

created: 2026-09-24

lastModified: 2026-09-26

state: proposed

instructions: |
  MUST set IsAotCompatible to true in every runtime package project under src/ (not in the netstandard2.0 source generator) and keep the build free of trim (IL2xxx), single-file (IL3000) and AOT (IL3050) warnings.
  MUST fix trim warnings at the root first: DynamicallyAccessedMembers on generic parameters and Type values, generic APIs instead of Type-based reflection, the configuration binding source generator instead of ConfigurationBinder reflection, and source-generated System.Text.Json contracts (JsonTypeInfo) for Pulse-owned DTOs.
  MUST annotate public entry points whose reflection is inherent and avoidable by the caller with RequiresUnreferencedCode and, where generic code is closed at runtime, RequiresDynamicCode, and list every such API in the "NativeAOT and Trimming" section of src/NetEvolve.Pulse/README.md.
  MAY use UnconditionalSuppressMessage only with a concrete justification that states why the reflection is safe or unreachable in trimmed applications; MUST NOT use SuppressMessage or #pragma for trim warnings.
  MUST keep samples/NetEvolve.Pulse.Xample.Aot and .github/workflows/aot.yml passing: the smoke application is published with NativeAOT for every target framework with warnings as errors and executed.
---

# Decision: NativeAOT and Trim Compatibility for Pulse Packages

Pulse packages are built as trim- and NativeAOT-compatible libraries. The analyzers run on every build, trim warnings are fixed at the root where feasible, inherently reflection-based APIs are annotated, and a NativeAOT smoke application verifies the core pipeline in CI.

## Context

Applications that publish trimmed, single-file or NativeAOT binaries rely on libraries that expose their reflection requirements to the trimmer. Before this decision, no Pulse project enabled the trim or AOT analyzers. Enabling `IsAotCompatible` in all runtime packages produced 100 distinct diagnostics per target framework (`net8.0`, `net9.0`, `net10.0`):

* `ConfigurationBinder.Bind` in the options configuration classes (IL2026/IL3050).
* Reflection-based `JsonSerializer` calls in `SystemTextJsonPayloadSerializer` and the outbox inspector (IL2026/IL3050).
* Generic registration methods without `DynamicallyAccessedMembers` on their implementation type parameter (IL2091).
* `Type.GetType` rehydration of outbox event types in all outbox providers and of command types in dead-letter replay (IL2057), plus `MakeGenericMethod` in dead-letter replay (IL2060/IL3050).
* `Validator.TryValidateObject` in the DataAnnotations interceptors (IL2026).
* Minimal API `Map*` calls that use `RequestDelegateFactory` (IL2026/IL3050).
* `Assembly.Location` in the XML documentation reader (IL3000) and `Assembly.GetTypes` in assembly scanning (IL2026).

Issue #297 additionally asks the source generator to emit `ILLink.Descriptors.xml` for the generated handler registrations, JSON source generation for Pulse-owned DTOs, and a CI step that publishes a NativeAOT test project without warnings.

The approach follows the Microsoft guidance [Prepare .NET libraries for trimming](https://learn.microsoft.com/dotnet/core/deploying/trimming/prepare-libraries-for-trimming), [Introduction to AOT warnings](https://learn.microsoft.com/dotnet/core/deploying/native-aot/fixing-warnings), [ASP.NET Core support for Native AOT](https://learn.microsoft.com/aspnet/core/fundamentals/native-aot) and [How to use source generation in System.Text.Json](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/source-generation).

## Decision

* Set `<IsAotCompatible>true</IsAotCompatible>` in every runtime package project. `Directory.Build.props` stays unchanged, and the netstandard2.0 source generator is excluded because it is a Roslyn component.
* Fix at the root where feasible:
  - Enable `EnableConfigurationBindingGenerator` in the packages that bind options from `IConfiguration`.
  - Resolve payload contracts in `SystemTextJsonPayloadSerializer` through `JsonSerializerOptions.GetTypeInfo`. Add the reflection resolver only while the `JsonSerializer.IsReflectionEnabledByDefault` feature switch is enabled. Trimmed and NativeAOT applications register their own source-generated context in `TypeInfoResolverChain`, because user payload types are out of scope.
  - Add an internal `PulseInspectorJsonSerializerContext` for the Pulse-owned DTOs written by the inspector endpoints (`OutboxMessage` and its list for the message and dead letter listings, `OutboxStatistics`, `AuditRecord` and its list, `AuditStatistics`, `CommandDeadLetterEntry` and its list, `CommandDeadLetterStatistics`, the dead letter count and the replay-all result). The inspector responses use `TypedResults.Json` with these contracts and the web defaults, independent of the application's `HttpJsonOptions`.
  - Add `DynamicallyAccessedMembers(PublicConstructors)` to generic registration type parameters.
* Annotate inherently reflection-based public entry points that callers can avoid with `RequiresUnreferencedCode` and `RequiresDynamicCode`. This covers assembly scanning, `AddDataAnnotations`, dead-letter replay (`ICommandDeadLetterManagement.ReplayAsync`, all implementations and `CommandDeadLetterReplayDispatcher.ReplayAsync`) and the ASP.NET Core `Map*` endpoint extensions.
* Suppress with `UnconditionalSuppressMessage` and a concrete justification only where the value is used safely:
  - The outbox `Type.GetType` site in `OutboxEventTypeResolver`, because only the type identity is used and unresolvable types are dead-lettered on fetch, see [Outbox Unresolvable Event Types](./2026-09-24-outbox-unresolvable-event-types.md).
  - The DataAnnotations interceptors, which are reachable only through the annotated registration.
  - The feature-switch-guarded reflection resolver.
  - The handled empty `Assembly.Location`.
* Do not emit `ILLink.Descriptors.xml` from the source generator. Roslyn source generators can only add C# sources, not MSBuild items or embedded resources, and the generated code already satisfies the trimmer. It emits generic `TryAdd*<TService, TImplementation>()` calls and `typeof(...)` literals, which satisfy the `DynamicallyAccessedMembers(PublicConstructors)` annotations of `Microsoft.Extensions.DependencyInjection`. `[DynamicDependency]` attributes would add nothing, so the generator output stays unchanged.
* Add `samples/NetEvolve.Pulse.Xample.Aot`, a `PublishAot` console application. It lives under `samples/` and uses the `.Xample` naming convention of `NetEvolve.Defaults`, so it is neither packed nor treated as a test project, in line with the [Folder Structure and Naming Conventions](./2025-07-10-folder-structure-and-naming-conventions.md). It exercises `AddPulse`, the generated registrations, open-generic handlers and interceptors, Send, Query, StreamQuery and Publish, the outbox event type round-trip and the registered payload serializer with an application `JsonSerializerContext`, and it reports failures through its exit code. `.github/workflows/aot.yml` publishes it for `linux-x64` per target framework with `-warnaserror` and runs the native binary. `PublishAot` is set in the project file instead of passing `-p:PublishAot=true` as issue #297 suggests, because the global property would also reach the netstandard2.0 source generator and fail with NETSDK1207.

## Consequences

* Trimmed and NativeAOT applications get precise warnings only for the Pulse APIs that are not trim-safe, instead of a generic IL2104 per assembly.
* The core mediator pipeline is verified with NativeAOT on every pull request.
* External implementers of `ICommandDeadLetterManagement` that enable the trim analyzer MUST add `RequiresUnreferencedCode` and `RequiresDynamicCode` to their `ReplayAsync` implementation (IL2046). There is no other source or binary impact.
* The inspector endpoints no longer honor custom `HttpJsonOptions`. They always write the web defaults (camelCase).
* Under NativeAOT, the DI container cannot close open-generic services over value types. Open-generic interceptors therefore fail for requests with value-type responses, including `Void`. This limitation is documented. Lifting it requires closed interceptor registrations and is tracked in #771.
* Outbox event types must be compiled into the application that reads the outbox. Since #772, a message with an unresolvable event type is dead-lettered on fetch with an error that names the stored type, and the other messages are still processed, see [Outbox Unresolvable Event Types](./2026-09-24-outbox-unresolvable-event-types.md).
* `NetEvolve.Pulse.AspNetCore.Grpc` is covered like every other runtime package. gRPC for ASP.NET Core is fully NativeAOT-compatible, and `MapStreamQueryGrpc` forwards the `DynamicallyAccessedMembers` requirement of `MapGrpcService`.
* `MapStreamQueryHub` is not annotated, because `MapHub` carries no `RequiresUnreferencedCode` or `RequiresDynamicCode` and the hub type is statically known. SignalR is not NativeAOT-supported on .NET 8 and only partially on .NET 9 and later; applications must register source-generated contracts for `TQuery` and `TResponse` with the JSON hub protocol. This is documented as a limitation.
* Provider packages remain limited by the NativeAOT support of their third-party dependencies.

## Alternatives Considered

* **Setting `IsAotCompatible` in `Directory.Build.props`**: rejected, because the file is protected by project policy and the property would also reach the netstandard2.0 generator and the test projects.
* **Propagating `RequiresUnreferencedCode` to `IOutboxRepository` and `IOutboxManagement`**: rejected, because it would mark the whole outbox pipeline as trim-unsafe although only the type identity is used.
* **Suppressing the dead-letter replay warnings**: rejected, because `MakeGenericMethod` over runtime types is not provably safe under NativeAOT, and replay is an optional administrative operation.
* **Request Delegate Generator in `NetEvolve.Pulse.AspNetCore`**: rejected for now. The generic `MapCommand` and `MapQuery` handlers cannot be intercepted, and enabling interceptors in a library across three target frameworks adds build complexity for little gain.
* **`[DynamicDependency]` on the generated registration method**: rejected as redundant, see the source generator decision above.

## Related Decisions

* [Folder Structure and Naming Conventions](./2025-07-10-folder-structure-and-naming-conventions.md) - The smoke application is an example application under `samples/`.

* [Centralized Package Version Management](./2025-07-10-centralized-package-version-management.md) - No new package versions are required. The configuration binding generator ships with the existing `Microsoft.Extensions.Configuration.Binder` package.
