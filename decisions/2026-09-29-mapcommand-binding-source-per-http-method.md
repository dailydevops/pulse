---
authors:
  - Martin Stühmer

applyTo:
  - "src/NetEvolve.Pulse.AspNetCore/**"

created: 2026-09-29

lastModified: 2026-09-29

state: accepted

instructions: |
  MUST bind MapCommand commands mapped to DELETE with [AsParameters] (route values and query string), like MapQuery; MUST NOT require or read a request body for DELETE.
  MUST keep [FromBody] binding for POST, PUT and PATCH and MUST overlay route values whose key matches a property of the command by JSON name or CLR name (case-insensitive) over the body values; the route value wins.
  MUST write a route value as JSON number or boolean literal only for numeric, bool and enum properties (nullable unwrapped); every other property type receives a JSON string.
  MUST convert route values with the application's HTTP JSON options (Microsoft.AspNetCore.Http.Json.JsonOptions), the same options the body binder uses.
  While the major version is 0, the behavioral break of this decision (DELETE bodies are no longer read) MUST NOT be marked with `!` or a `BREAKING CHANGE:` footer, because GitVersion would bump to 1.0.0; it MUST be listed under "Impact" in the PR description. This extends the 0.x exception of 2026-09-24-extensibility-interface-evolution-pre-1-0.md to this AspNetCore behavioral change.
---

# Decision: MapCommand Binding Source per HTTP Method

`MapCommand` binds `DELETE` commands from route values and the query string, and binds `POST`, `PUT` and `PATCH` commands from the request body with matching route values overlaid over the body. The route value wins.

## Context

Both `MapCommand` overloads bound the command only with `[FromBody]` (#848). Route values were never read, although the XML documentation and the README map `/orders/{id}` and declare `record DeleteOrderCommand(Guid Id)`.

* A `DELETE /orders/{id}` without a body was rejected with `415 Unsupported Media Type` (no `Content-Type`) or `400 Bad Request` (empty JSON body) before the handler ran.
* `PUT /orders/A` with body `{"id":"B"}` dispatched the command with `Id = B`. The command acted on a different resource than the URI names, which is an IDOR-style risk when authorization is based on the route.

[RFC 9110 §9.3.5 DELETE](https://www.rfc-editor.org/rfc/rfc9110#section-9.3.5) states:

> Although request message framing is independent of the method used, content received in a DELETE request has no generally defined semantics, cannot alter the meaning or target of the request, and might lead some implementations to reject the request and close the connection because of its potential as a request smuggling attack. A client SHOULD NOT generate content in a DELETE request unless it is made directly to an origin server that has previously indicated, in or out of band, that such a request has a purpose and will be adequately supported.

[Parameter binding in Minimal API apps](https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/parameter-binding) states that `GET`, `HEAD`, `OPTIONS` and `DELETE` do not implicitly bind from the body, and that `[AsParameters]` binds route values, query string and headers into the properties and constructor parameters of a type.

## Decision

* `CommandHttpMethod.Delete` binds the command with `[AsParameters]`, as `MapQuery` does. A body sent with a `DELETE` request is ignored.
* `CommandHttpMethod.Post`, `Put` and `Patch` keep `[FromBody]`. The framework keeps handling `415`, `400` for a missing or malformed body, and the OpenAPI request body metadata.
* After body binding, every route value whose key matches a property of `TCommand` case-insensitively replaces that property. A key matches the JSON property name (after the naming policy and `[JsonPropertyName]`) or the CLR member name, so `/orders/{orderId}` targets `OrderId` also with `JsonNamingPolicy.SnakeCaseLower`. The command is serialized to a `JsonObject`, the matched properties are replaced and the object is deserialized again with the application's `Microsoft.AspNetCore.Http.Json.JsonOptions`. For numeric, `bool` and enum properties (nullable unwrapped), route values that parse as number or boolean are written as JSON literals, so they bind without `JsonNumberHandling.AllowReadingFromString`. Every other property type receives a JSON string, so a digit-only route value still binds to a strongly typed ID whose converter reads a string.
* A request without a matching route value dispatches the bound command unchanged, without the round-trip.

## Consequences

* Body-less `DELETE` requests reach the handler, and the command targets the resource named in the URI for every method.
* A client that sent the identifier only in a `DELETE` body must move it to the route or the query string. Values sent in a `DELETE` body are no longer read. This is a behavioral break for such clients.
* The behavioral break is not marked with `!` or a `BREAKING CHANGE:` footer while the major version is `0`, because GitVersion would bump the version to `1.0.0`. It is listed under "Impact" in the pull request instead, as [Extensibility Interface Evolution Before 1.0](./2026-09-24-extensibility-interface-evolution-pre-1-0.md) does for interface changes. Accepting this decision records that exception for this change.
* `DELETE` commands must satisfy the `[AsParameters]` rules: every property needs a route, query or header source with a `TryParse` or `BindAsync` binding; complex properties fail when the endpoint is built.
* Commands bound from the body with matching route values are serialized and deserialized once more per request. Properties that do not round-trip through the application's JSON options (for example write-only or `JsonIgnore`d properties set by the body) lose their value when a route value is overlaid.
* The `Map*` methods keep their `RequiresUnreferencedCode` and `RequiresDynamicCode` annotations; the reflection-based JSON round-trip is covered by them.

## Alternatives Considered

* **Keep body binding and document that route values are ignored**: rejected, because the documented `/orders/{id}` examples would still let the body retarget the command, and body-less `DELETE` would still fail.
* **Reject a mismatch between route and body with `400 Bad Request`**: rejected, because it requires a type-aware comparison per property and still forces clients to repeat the identifier in the body.
* **`[AsParameters]` for every method**: rejected, because it drops body binding for `POST`, `PUT` and `PATCH`.
* **Read the body manually into a `JsonObject` and merge before the first deserialization**: rejected, because it reimplements the framework's `415` and `400` handling and loses the OpenAPI request body metadata.

## Related Decisions

* [Honor Application JSON Options for Pulse-Owned Models](./2026-09-27-honor-application-json-options.md) - The overlay uses the application's HTTP JSON options, like the inspector endpoints.
* [Extensibility Interface Evolution Before 1.0](./2026-09-24-extensibility-interface-evolution-pre-1-0.md) - The 0.x exception to the breaking change marker is extended to the behavioral break of this decision.
* [Conventional Commits](./2025-07-10-conventional-commits.md) - The breaking change marker rule does not apply to this change while the major version is `0`.
* [NativeAOT and Trim Compatibility](./2026-09-24-nativeaot-trim-compatibility.md) - The `Map*` annotations stay unchanged.
