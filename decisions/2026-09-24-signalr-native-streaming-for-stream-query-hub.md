---
authors:
  - Martin Stühmer

applyTo:
  - "src/NetEvolve.Pulse.AspNetCore/**/*.cs"

created: 2026-09-24

lastModified: 2026-09-26

state: accepted

instructions: |
  SignalR hubs that expose Pulse stream queries MUST use native SignalR server-to-client streaming:
  a hub method that returns IAsyncEnumerable<TResponse> and takes a CancellationToken. They MUST NOT push
  items with Clients.Caller.SendAsync. Generic types MUST be closed on the hub class
  (PulseStreamHub<TQuery, TResponse>), because SignalR rejects generic hub methods.
---

# Decision: Native SignalR Streaming for the Stream Query Hub

`PulseStreamHub<TQuery, TResponse>` exposes `IMediator.StreamQueryAsync` through a native SignalR streaming hub method, `IAsyncEnumerable<TResponse> StreamAsync(TQuery, CancellationToken)`. Issue #298 proposed pushing each item with `Clients.Caller.SendAsync("OnItem", item)`; this hub does not do that.

## Context

Issue #298 asks for a SignalR hub that streams `IMediator.StreamQueryAsync` results to browser clients. It has two acceptance criteria:

- "Hub iterates the stream and sends all items to the caller."
- "Client cancellation stops the stream without exceptions."

The issue sketched two things:

- A generic hub method, `StreamAsync<TQuery, TResponse>(TQuery query)`.
- An item push via `Clients.Caller.SendAsync("OnItem", item)`.

The SignalR documentation and hub dispatcher constrain both of these:

- SignalR does not support generic hub methods, and `MapHub<THub>` needs a closed hub type.
- A normal (non-streaming) hub invocation cannot be cancelled by the client. The only cancellation signal it gets is `Context.ConnectionAborted`, which fires when the whole connection drops.
- A long-running non-streaming invocation blocks every other invocation from the same client, unless `HubOptions.MaximumParallelInvocationsPerClient` is greater than 1.
- With `SendAsync("OnItem")`, the client gets no completion signal and no error signal for the stream.
- A streaming hub method returns `IAsyncEnumerable<T>` or `ChannelReader<T>`. SignalR cancels its `CancellationToken` parameter when the client unsubscribes, for example with `subscription.dispose()` in JavaScript or by cancelling the token passed to `HubConnection.StreamAsync` in .NET. Streaming invocations do not block the client's other hub calls. The stream completion and any error reach the client through the protocol. Source: [Use streaming in ASP.NET Core SignalR](https://learn.microsoft.com/aspnet/core/signalr/streaming).

## Decision

- `PulseStreamHub<TQuery, TResponse> : Hub where TQuery : IStreamQuery<TResponse>` closes the generic types on the hub class.
- The hub method is `public IAsyncEnumerable<TResponse> StreamAsync(TQuery query, CancellationToken cancellationToken)`. It passes the token to `IMediator.StreamQueryAsync`.
- If an `OperationCanceledException` is thrown while that token is cancelled, the hub ends the stream normally. Any other exception propagates, including an `OperationCanceledException` from an unrelated token. This is the same pattern `MapStreamQuery` uses for HTTP streaming.
- `MapStreamQueryHub<TQuery, TResponse>(this IEndpointRouteBuilder, string path)` maps the hub with `MapHub` and returns the `HubEndpointConventionBuilder`. `services.AddSignalR()` is a documented prerequisite.

## Consequences

- Both acceptance criteria are met. All items reach the caller, and a client unsubscribe stops the stream without an exception.
- Clients use `connection.stream("StreamAsync", query)` in JavaScript or `HubConnection.StreamAsync<T>("StreamAsync", query, token)` in .NET. They do not register an `OnItem` handler.
- Each query type needs its own hub path.
- No new package reference is needed, because server-side SignalR ships with the `Microsoft.AspNetCore.App` shared framework.

## Alternatives Considered

- **Push items with `Clients.Caller.SendAsync("OnItem", item)`, as the issue text describes.** Rejected. The client cannot cancel a single stream, only disconnect. The call also blocks the client's other invocations, and there is no completion or error signal.
- **Offer both styles.** Rejected. Two public APIs for the same job would be a speculative abstraction.
- **Return `ChannelReader<T>`.** Rejected. The SignalR docs recommend async iterators because a channel must be returned early and completed correctly, which is error-prone.

## Related Decisions

- [.NET 10 and C# 13 Adoption](./2025-07-11-dotnet-10-csharp-13-adoption.md): async iterators and `[EnumeratorCancellation]` are standard language features on every supported target framework.
