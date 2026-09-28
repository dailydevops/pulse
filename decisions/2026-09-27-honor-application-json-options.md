---
authors:
  - Martin Stühmer

applyTo:
  - "src/NetEvolve.Pulse.AspNetCore/**"
  - "src/NetEvolve.Pulse.AzureQueueStorage/**"

created: 2026-09-27

lastModified: 2026-09-28

state: accepted

instructions: |
  MUST serialize Pulse-owned models that are written through an application-facing channel (inspector HTTP responses, transport envelopes) with the application's configured JSON options, not with a Pulse-only context.
  MUST copy the configured JsonSerializerOptions once per options instance and append the internal source-generated JsonSerializerContext to TypeInfoResolverChain as fallback resolver; MUST NOT mutate the configured instance.
  MUST NOT force an enum representation in inspector responses; enums follow the application's options (numbers by default).
  MUST keep the transport envelope shape stable: explicit JsonPropertyName attributes, and DefaultIgnoreCondition, ReferenceHandler and WriteIndented reset on the copied options; document the wire format in the package README.
---

# Decision: Honor Application JSON Options for Pulse-Owned Models

The inspector endpoints and the Azure Queue Storage envelope serialize with the application's configured JSON options. An internal source-generated context is appended as fallback resolver, so NativeAOT applications need no reflection and no application contract for Pulse-owned models.

## Context

[NativeAOT and Trim Compatibility](./2026-09-24-nativeaot-trim-compatibility.md) introduced the internal `PulseInspectorJsonSerializerContext` for the outbox, audit and command dead letter inspectors and `AzureQueueStorageJsonSerializerContext` for the queue envelope. Both were used directly, so:

* The inspector responses ignored the application's `HttpJsonOptions` (naming policy, converters, enum handling). Property names were always camelCase, and enums were always written as numbers.
* The Azure Queue Storage envelope ignored the configured `JsonSerializerOptions`. Before #770 it was written through `IPayloadSerializer` and therefore honored them.

Clients that rely on the application's JSON conventions broke silently (#794).

The approach follows [How to use source generation in System.Text.Json](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/source-generation#combine-source-generators) and [ASP.NET Core support for Native AOT](https://learn.microsoft.com/aspnet/core/fundamentals/native-aot#work-with-minimal-apis-and-json-payloads): source-generated contexts are combined through `JsonSerializerOptions.TypeInfoResolverChain`.

## Decision

* The inspector endpoints resolve `IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>` and serialize with a copy of its `SerializerOptions`. The copy is created once per options instance and cached in a `ConditionalWeakTable`. It appends:
  - `TypeJsonConverter` for `OutboxMessage.EventType`.
  - `PulseInspectorJsonSerializerContext.Default` at the end of `TypeInfoResolverChain`.
* The Azure Queue Storage transport resolves `IOptions<JsonSerializerOptions>`, the options of the default `IPayloadSerializer`, copies them once, resets `DefaultIgnoreCondition` to `Never`, `ReferenceHandler` to `null` and `WriteIndented` to `false`, appends `AzureQueueStorageJsonSerializerContext.Default` to `TypeInfoResolverChain` and writes the envelope with the resulting `JsonTypeInfo`.
* The envelope properties carry explicit `JsonPropertyName` attributes (`id`, `eventType`, `payload`, `correlationId`, `causationId`, `createdAt`), and the optional identifiers carry `JsonIgnore(Condition = Never)`. A naming policy, `DefaultIgnoreCondition`, `ReferenceHandler.Preserve` (`$id`) or `WriteIndented` therefore cannot change the wire format or its size against the 48 KB limit. The wire format is documented in the package README.

## Consequences

* Inspector responses follow the application's naming policy and converters. Application converters precede the appended ones, so an application can still control enum and `Type` handling.
* Inspector enums keep the numeric output of the #770 contract by default. Applications that want strings register `JsonStringEnumConverter<TEnum>` through `ConfigureHttpJsonOptions`.
* The envelope honors converters and the encoder of the configured options, for example for `createdAt`, while its shape stays stable.
* NativeAOT applications need no reflection and no application context for inspector responses or the envelope. The fallback resolver serves the Pulse-owned models when no earlier resolver does.
* The configured options instances are never mutated, so shared or read-only options are safe.

## Alternatives Considered

* **Keep the internal context as the only resolver**: rejected, because it breaks clients that rely on the application's JSON conventions.
* **Insert the internal context at the start of the chain**: rejected, because application resolvers and modifiers could no longer customize the Pulse-owned models.
* **Mutate the configured options in place**: rejected, because the instance is shared, may already be read-only, and concurrent mutation is racy.
* **Annotate the Pulse enums with `JsonConverter` attributes**: rejected, because the enums live in `NetEvolve.Pulse.Extensibility` and are serialized by other channels that must not change.
* **Write inspector enums as strings by default**: rejected, because it would silently change the numeric output existing clients read. Applications opt in through `ConfigureHttpJsonOptions` instead.

## Related Decisions

* [NativeAOT and Trim Compatibility](./2026-09-24-nativeaot-trim-compatibility.md) - Introduced the internal source-generated contexts that this decision turns into fallback resolvers.
