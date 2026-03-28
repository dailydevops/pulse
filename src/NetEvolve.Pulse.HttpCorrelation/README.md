# NetEvolve.Pulse.HttpCorrelation

HTTP correlation ID propagation interceptors for the Pulse CQRS mediator library. Automatically propagates the HTTP correlation ID from `IHttpCorrelationAccessor` into every `IRequest<TResponse>` and `IEvent` dispatched through the mediator.

## Installation

```shell
dotnet add package NetEvolve.Pulse.HttpCorrelation
```

## Usage

Register the interceptors once at startup:

```csharp
services.AddPulse(c => c.AddHttpCorrelationEnrichment());
```

The interceptors automatically propagate `IHttpCorrelationAccessor.CorrelationId` into `request.CorrelationId` (and `event.CorrelationId`) when:

- The request/event's `CorrelationId` is `null` or empty.
- `IHttpCorrelationAccessor.CorrelationId` is non-`null` and non-empty.

Existing non-empty `CorrelationId` values are never overwritten.

If `IHttpCorrelationAccessor` is not registered (e.g. in a background-service context), both interceptors pass through without modification or error.
