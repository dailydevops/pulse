---
authors:
  - Martin Stühmer

applyTo:
  - "src/NetEvolve.Pulse/NativeAotInterceptorExtensions.cs"
  - "src/NetEvolve.Pulse/Internals/PulseMediator.cs"
  - "src/NetEvolve.Pulse/**/*Extensions.cs"
  - "src/NetEvolve.Pulse.SourceGeneration/**/*.cs"
  - "samples/NetEvolve.Pulse.Xample.Aot/**"

created: 2026-09-24

lastModified: 2026-09-24

state: proposed

instructions: |
  MUST make requests with value-type request or response types pass through the built-in open-generic interceptors under NativeAOT by registering closed keyed interceptors per request type: NetEvolve.Pulse.SourceGeneration emits one NativeAotInterceptorExtensions call per handled value-type request when the compilation references NetEvolve.Pulse, and the mediator resolves the keyed interceptors for value-type requests when they are registered.
  MUST add every new built-in open-generic request or stream query interceptor to NativeAotInterceptorExtensions, and MUST register closed built-in services (never open-generic self-registrations) in closed per-request registration methods such as AddConcurrentCommandGuard<TRequest, TResponse>.
---

# Decision: Closed Keyed Interceptors for Value-Type Requests Under NativeAOT

Under NativeAOT, Pulse closes the built-in open-generic interceptors for every handled request with a value-type request or response type at registration time and registers them as keyed services. The mediator resolves these keyed interceptors for such requests. The source generator emits the registrations for the handlers it registers.

## Context

`Microsoft.Extensions.DependencyInjection` cannot close open-generic services over value types under NativeAOT. When `RuntimeFeature.IsDynamicCodeSupported` is `false`, `CallSiteFactory.CreateOpenGeneric` calls `VerifyOpenGenericAotCompatibility` before `MakeGenericType` and throws an `InvalidOperationException` ("Unable to create a generic service for type '…' because '…' is a ValueType") for every value-type generic argument. `CreateOpenGeneric` only catches `ArgumentException`, so the exception also escapes the enumerable resolution in `TryCreateEnumerable`. This behavior is identical in .NET 8, 9 and 10.

Pulse registers its built-in interceptors as open generics (`AddActivityAndMetrics`, `AddLogging`, `AddQueryCaching`, `AddRequestTimeout`, and others). As soon as one of them is registered, `GetServices<IRequestInterceptor<TRequest, TResponse>>()` throws for every request with a value-type request or response type, including `Void` for commands without a result. The same applies to `IStreamQueryInterceptor<TQuery, TResponse>` with value-type items. Closed registrations added next to the open-generic ones do not help, because the open-generic descriptor is still visited during the enumerable resolution. Issue #771 tracks the limitation, which the NativeAOT smoke application added in #770 found.

A JIT run of `samples/NetEvolve.Pulse.Xample.Aot` reproduces the failure, because `PublishAot` writes the `System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported=false` switch to the runtime configuration. `.github/workflows/aot.yml` publishes and runs the native binaries.

## Decision

* Add the public class `NativeAotInterceptorExtensions` to `NetEvolve.Pulse` with one generic method per request kind: `AddNativeAotCommandInterceptors<TCommand, TResponse>`, `AddNativeAotExclusiveCommandInterceptors<TCommand, TResponse>`, `AddNativeAotQueryInterceptors<TQuery, TResponse>` and `AddNativeAotStreamQueryInterceptors<TQuery, TResponse>`. The kinds exist because the concurrent command guard and query caching constrain their request type to `IExclusiveCommand<TResponse>` and `IQuery<TResponse>`.
* Each method does nothing while `RuntimeFeature.IsDynamicCodeSupported` is `true`. Otherwise, it walks the service collection in registration order:
  - Each open-generic built-in interceptor becomes a closed keyed descriptor with the same lifetime. The closed type is a `typeof` literal in generic code, for example `typeof(LoggingRequestInterceptor<TRequest, TResponse>)`, so no `MakeGenericType` is needed and the NativeAOT compiler generates the instantiation.
  - Each closed interceptor for the same request type is copied as a keyed descriptor.
  - A built-in interceptor whose constraint does not apply to the request kind is skipped. The generic `Close*` methods are the only list of built-in interceptors: they return the closed type, a "not applicable" sentinel, or `null` for an unknown interceptor.
  - When an open-generic interceptor that Pulse does not know is registered (FluentValidation, HTTP correlation, application interceptors), nothing is registered for the request type. Resolving its interceptors keeps failing loudly instead of silently skipping, for example, a validation interceptor.
  - When no interceptor is registered for the request type, nothing is registered, so the existing resolution also covers interceptors registered later.
  - A keyed marker, keyed by the closed interceptor service type, records the registration and makes repeated calls idempotent. It keeps the service collection and the unkeyed interceptor registrations for the request type that the keyed registrations were built from.
* The mediator resolves the marker through `IKeyedServiceProvider` for requests with a value-type request or response type, which is one lookup per dispatch, and resolves the keyed interceptors when it is present. Before the first use, it compares the current unkeyed interceptor registrations for the request type with those recorded by the marker and throws an `InvalidOperationException` when an interceptor was registered, replaced or removed afterwards, instead of silently skipping it. All other requests, and containers without keyed service support, keep the existing resolution. The check is not gated on `RuntimeFeature`, so JIT unit tests cover it.
* `NetEvolve.Pulse.SourceGeneration` emits one `NativeAotInterceptorExtensions` call per handled command, query or stream query with a value-type request or response type at the end of the generated registration method, but only when the compilation references `NetEvolve.Pulse`. Handler assemblies that only reference `NetEvolve.Pulse.Extensibility`, and applications with reference-type requests only, generate the same code as before.
* `AddConcurrentCommandGuard<TRequest, TResponse>()` registers the closed `ConcurrentCommandGuardInterceptor<TRequest, TResponse>` instead of an open-generic self-registration, which failed under NativeAOT for every exclusive command with a `Void` response.
* The NativeAOT smoke application covers a value-type command, a `Void` command, overlapping exclusive `Void` commands serialized by the concurrent command guard, and a value-type stream query through the built-in interceptors and a closed application interceptor.

## Consequences

* Requests with value-type request or response types pass through the built-in interceptors of `NetEvolve.Pulse` under NativeAOT, provided their handlers are registered by the source generator.
* The generated registration method MUST run after `AddPulse(...)` and after every interceptor registration. When an interceptor for a value-type request is registered, replaced or removed later, the mediator throws an `InvalidOperationException` for that request under NativeAOT. Modifying the service collection after building the service provider triggers the same exception.
* Open-generic interceptors of other packages or of the application still fail for value-type requests under NativeAOT. Such requests keep the existing loud failure.
* Handlers registered without the source generator still fail for value-type requests when open-generic interceptors are registered. Applications can call the `NativeAotInterceptorExtensions` methods themselves.
* Every NativeAOT application with a value-type request roots the built-in request and stream query interceptor types, which increases the binary size slightly.
* JIT applications are unaffected: the methods register nothing, and the mediator only reads the marker for value-type requests.
* `NativeAotInterceptorExtensions` is a new public API, called by generated code and by applications for handlers registered without the source generator.

## Alternatives Considered

* **Closed registrations next to the open-generic ones**: rejected, because the open-generic descriptor is still visited during the enumerable resolution and throws.
* **Removing the open-generic descriptors after closing them for the known request types**: rejected. With more than one handler assembly, the first generated method removes the descriptors that the second one needs, and handlers unknown to the generator, including reference-type ones, would silently lose all built-in interceptors.
* **Generic `Add*Interceptor<TRequest, TResponse>()` overloads for each built-in interceptor**: rejected as the primary path, because they hit the same open-generic resolution failure and require one call per request type and interceptor in application code.
* **Closing interceptors at resolution time with `ActivatorUtilities`**: rejected, because Pulse would have to reimplement the singleton and scoped lifetimes of the DI container.
* **A table-free design with a generic virtual method per built-in extension**: rejected for now as speculative. The fixed list of built-in interceptors is small and lives next to the interceptors.

## Related Decisions

* [NativeAOT and Trim Compatibility for Pulse Packages](./2026-09-24-nativeaot-trim-compatibility.md) - This decision lifts the value-type interceptor limitation recorded there.
