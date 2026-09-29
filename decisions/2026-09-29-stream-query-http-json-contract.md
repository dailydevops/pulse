---
authors:
  - Martin Stühmer

applyTo:
  - "src/NetEvolve.Pulse.AspNetCore/**"

created: 2026-09-29

lastModified: 2026-09-29

state: proposed

instructions: |
  MUST serialize MapStreamQuery items for NDJSON and SSE with a copy of the application's Microsoft.AspNetCore.Http.Json.JsonOptions.SerializerOptions that has WriteIndented = false; MUST NOT use IPayloadSerializer for HTTP stream items.
  MUST write every item as exactly one single-line JSON text (NDJSON: text + LF; SSE: "data: " + text + LF LF), including string items, which are written as quoted JSON strings on every target framework.
  MUST resolve contracts through JsonSerializerOptions.GetTypeInfo (TypeInfoResolverChain) and fall back to the runtime type contract like TypedResults.Ok when the declared type is not sealed, not a value type and has no polymorphism options.
  MUST set Cache-Control no-cache,no-store and Content-Encoding identity and disable response buffering for SSE on every target framework.
---

# Decision: One JSON Contract for MapStreamQuery Items

`MapStreamQuery` serializes every streamed item with the application's HTTP JSON options (`ConfigureHttpJsonOptions`), forced to single-line output, and writes it as one JSON text per NDJSON line or per SSE `data:` line. This reverses, for this endpoint, the choice from #681 to use `IPayloadSerializer`.

## Context

Since #681, `MapStreamQuery` wrote NDJSON (all target frameworks) and SSE (net8.0, net9.0) with `IPayloadSerializer`, while SSE on net10.0 used `TypedResults.ServerSentEvents` (#846):

* `IPayloadSerializer` reads `IOptions<JsonSerializerOptions>`, not the HTTP JSON options. Items were PascalCase and ignored the application's naming policy and converters, while `MapQuery`, `MapCommand` and net10 SSE wrote camelCase ("By default, Minimal API apps use Web defaults options during JSON serialization", [Minimal API responses](https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/responses#configure-json-serialization-options)).
* With `WriteIndented = true` an item spanned several lines. The [NDJSON spec §3.1](https://github.com/ndjson/ndjson-spec#31-serialization) says "The JSON texts MUST NOT contain newlines or carriage returns", and [WHATWG HTML §9.2.6](https://html.spec.whatwg.org/multipage/server-sent-events.html#event-stream-interpretation) ignores lines without a `data` field name, so the pre-net10 SSE client received only `{`.
* `ServerSentEventsResult` on net10 writes strings "as raw strings without any additional formatting" ([TypedResults.ServerSentEvents](https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.http.typedresults.serversentevents)), so a `string` item was `data: text` on net10 but `data: "text"` everywhere else.
* The pre-net10 SSE path did not set the `Cache-Control: no-cache,no-store` and `Content-Encoding: identity` headers or disable response buffering, which `ServerSentEventsResult` does on net10.

## Decision

* `MapStreamQuery` resolves `IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>` and serializes items with a copy of its `SerializerOptions` that has `WriteIndented = false`. The copy is created once per options instance and cached in a `ConditionalWeakTable`, as in [Honor Application JSON Options](./2026-09-27-honor-application-json-options.md). The configured instance is never mutated.
* Contracts come from `JsonSerializerOptions.GetTypeInfo(typeof(TResponse))`. When an item's runtime type differs and the declared contract is not sealed, not a value type and has no `PolymorphismOptions`, the runtime type contract is used, the same rule `TypedResults.Ok` and `ServerSentEventsResult` apply.
* Every item is one single-line JSON text: NDJSON writes the text and `\n`; SSE writes `data: `, the text and `\n\n`. `string` items are quoted JSON strings on every format and target framework.
* On net10.0 the pre-serialized texts are passed to `TypedResults.ServerSentEvents`, so the framework's SSE headers and formatter stay in use. On net8.0 and net9.0 the endpoint writes the same bytes and sets the same headers itself.

## Consequences

* NDJSON and SSE items use the same property names as `MapQuery` and `MapCommand` on every target framework. **Breaking:** NDJSON clients, and SSE clients on net8.0/net9.0, receive camelCase (or the configured naming policy) instead of PascalCase. SSE clients on net10.0 receive `string` items as quoted JSON strings instead of raw text, and a `null` item as `data: null` instead of an empty `data:` line, the same as NDJSON.
* NDJSON and SSE output stays valid regardless of `WriteIndented`.
* `MapStreamQuery` no longer depends on `IPayloadSerializer`. A custom `IPayloadSerializer` no longer affects HTTP stream items; applications customize them through `ConfigureHttpJsonOptions`.
* NativeAOT applications add their item types to the HTTP JSON options' `TypeInfoResolverChain`, the same requirement `MapQuery` has. No reflection-only API is used.
* Known limit: an application converter that emits raw newlines through `Utf8JsonWriter.WriteRawValue` can still break the framing. This is not guarded.

## Alternatives Considered

* **Keep `IPayloadSerializer` and fix only the framing**: rejected, because the casing would still differ from `MapQuery` and from net10 SSE.
* **Keep the configured `WriteIndented` and split SSE data per line like `SseFormatter`**: rejected, because NDJSON cannot carry multi-line texts and both formats should deliver the same bytes per item.
* **Keep raw `string` items on net10 SSE**: rejected, because NDJSON requires JSON texts and the output would still differ between target frameworks.
* **Write SSE manually on net10 as well**: rejected, because `TypedResults.ServerSentEvents` already provides the headers and formatting.

## Related Decisions

* [Honor Application JSON Options for Pulse-Owned Models](./2026-09-27-honor-application-json-options.md) - Same options copy pattern, applied here to application stream items.
* [NativeAOT and Trim Compatibility](./2026-09-24-nativeaot-trim-compatibility.md) - Contracts are resolved through `TypeInfoResolverChain`.
* [Wire and Behavior Changes Before 1.0](./2026-09-29-wire-and-behavior-changes-pre-1-0.md) - Why the breaking wire change is committed without a breaking change marker.
