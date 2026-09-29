---
authors:
  - Martin Stühmer

applyTo:
  - "src/NetEvolve.Pulse/Internals/PulseMediator.cs"
  - "src/NetEvolve.Pulse/Dispatchers/*.cs"
  - "src/NetEvolve.Pulse/Outbox/OutboxEventHandler*.cs"

created: 2026-09-29

lastModified: 2026-09-29

state: accepted

instructions: |
  MUST invoke OutboxEventHandler<TEvent> in PulseMediator sequentially and before the configured IEventDispatcher, inside the event interceptor chain, and MUST pass only the remaining handlers to the dispatcher.
  MUST keep the handler error contract: every handler runs, and failures are thrown afterwards as one flat AggregateException.
  MUST keep ParallelEventDispatcher as the default and document that user handlers sharing a scoped DbContext or connection need SequentialEventDispatcher.
---

# Decision: The Outbox Handler Runs Before the Event Dispatcher

`PulseMediator.PublishAsync` runs the framework's `OutboxEventHandler<TEvent>` on its own and in order, before the configured `IEventDispatcher` gets the other handlers. The outbox write never overlaps another handler in the caller's scope.

## Context

Since #599, event handlers are resolved from the caller's scope, so scoped handlers share the caller's `DbContext` and transaction. `AddOutbox()` registers `OutboxEventHandler<>` as an open generic for every event. With the Entity Framework outbox it calls `AddAsync` and `SaveChangesAsync` on the scoped `TContext`.

The default `ParallelEventDispatcher` ran all resolved handlers through `Parallel.ForEachAsync`. The outbox handler always adds a second handler, so an event with a single user handler never took the single-handler fast path. A user handler that used the same `DbContext` therefore ran concurrently with the outbox write. EF Core does not support that ([Avoiding DbContext threading issues](https://learn.microsoft.com/ef/core/dbcontext-configuration/#avoiding-dbcontext-threading-issues)). It fails intermittently with "A second operation was started on this context instance", or it corrupts the unit of work without any error (#812).

## Decision

- `PulseMediator` splits the resolved handlers into the outbox handlers (`is OutboxEventHandler<TEvent>`) and the rest.
- Inside the innermost step of the event interceptor chain, it invokes the outbox handlers one after another. It then passes only the remaining handlers to the resolved dispatcher: a keyed per-event dispatcher, the global one, or the parallel default. Interceptors such as event filters therefore still wrap and can suppress the outbox write.
- The error contract does not change. An outbox failure does not stop the other handlers. All failures are thrown together as one flat `AggregateException` after every handler has run.
- Events without an outbox handler take the unchanged path.
- `ParallelEventDispatcher` remains the default. Its documentation and the READMEs now state that user handlers sharing a scoped `DbContext` or connection must use `UseEventDispatcherFor<TEvent, SequentialEventDispatcher>()` (or `UseDefaultEventDispatcher<SequentialEventDispatcher>()` for all events).

## Consequences

- With the default configuration, the outbox write never runs concurrently with a user handler, for every dispatcher.
- An outbox plus exactly one user handler now takes the parallel dispatcher's single-handler fast path.
- The outbox write always happens first, including with `SequentialEventDispatcher`. Before, the order followed registration order. Nothing documented relied on the old order.
- Several user handlers that share one `DbContext` can still collide under the parallel default. That is the documented contract of `ParallelEventDispatcher`, and the documented workaround is `SequentialEventDispatcher`.
- The dispatcher decides nothing for the outbox handler, so a custom dispatcher (for example rate limiting or priorities) no longer sees it.
- A secondary effect is not changed by this decision. `EntityFrameworkOutboxRepository.AddAsync` calls `SaveChangesAsync` on the shared context, so it also commits changes the caller has made but not yet saved. Running the outbox first at least keeps it from flushing half-built changes of other handlers. The Entity Framework README documents this.

## Alternatives Considered

- **Make `SequentialEventDispatcher` the default.** This is safe for all shared scoped services, but it is a breaking behavioral change for every consumer, and it removes the throughput of independent handlers. It was rejected in favour of the smaller, targeted fix. Consumers can still opt in.
- **Fix the single-handler case inside `ParallelEventDispatcher`.** This only covers one dispatcher. Keyed, rate-limited, prioritized and custom dispatchers would still run the outbox concurrently.
- **Run the outbox handler after the other handlers.** Its `SaveChangesAsync` would then commit whatever the user handlers left pending. Running it first keeps the flush limited to the caller's state.
- **Add a marker interface for scope-sharing framework handlers.** The outbox handler is currently the only one, so that would be a speculative abstraction.

## Related Decisions (Optional)

- [Extensibility Interface Evolution Pre-1.0](./2026-09-24-extensibility-interface-evolution-pre-1-0.md) - No public interface changes; this is a behavioral fix inside the mediator.
