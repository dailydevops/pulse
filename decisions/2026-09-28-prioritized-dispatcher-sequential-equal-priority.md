---
authors:
  - Martin Stühmer

applyTo:
  - "src/NetEvolve.Pulse/Dispatchers/PrioritizedEventDispatcher.cs"
  - "src/NetEvolve.Pulse.Extensibility/IPrioritizedEventHandler{TEvent}.cs"

created: 2026-09-28

lastModified: 2026-09-28

state: proposed

instructions: |
  PrioritizedEventDispatcher MUST execute handlers one at a time, ordered by ascending Priority, with equal-priority handlers in registration (enumeration) order; non-prioritized handlers count as int.MaxValue.
  MUST NOT run handlers concurrently. Caller cancellation MUST surface as OperationCanceledException (catch filter `when (!cancellationToken.IsCancellationRequested)`), other handler failures are collected into one AggregateException.
---

# Decision: Prioritized Event Dispatcher Runs Equal-Priority Handlers Sequentially

`PrioritizedEventDispatcher` runs every handler sequentially. It orders them by ascending `Priority` and keeps registration order for equal priorities. This matches the contract published on `IPrioritizedEventHandler<TEvent>`.

## Context

Issue #828 shows two conflicting contracts for handlers with equal priority:

- `IPrioritizedEventHandler<TEvent>` (public contract in `NetEvolve.Pulse.Extensibility`): "Handlers with equal priority execute in registration order".
- `PrioritizedEventDispatcher`: groups handlers by priority and runs each group with `Parallel.ForEachAsync`, so equal-priority handlers run concurrently in no defined order.

Implementers of prioritized handlers read the interface, not the dispatcher internals. A handler that relies on the documented order, for example two priority-0 validators where the second assumes the first has already run, gets a race condition.

## Decision

- Handlers are sorted with a stable sort (`Enumerable.OrderBy`) by priority and invoked one after another. Equal priorities keep the order in which the handlers were enumerated, which is registration order for the default DI container.
- Handlers that do not implement `IPrioritizedEventHandler<TEvent>` count as priority `int.MaxValue`, so they run last, also in registration order.
- The interface contract stays unchanged. The dispatcher documentation is updated to match it.

## Consequences

- Handler order is fully deterministic and matches the published contract.
- Equal-priority handlers no longer overlap in time, so a group with several slow handlers takes longer. Applications that need concurrency within a priority level should use `ParallelEventDispatcher` or `RateLimitedEventDispatcher`, or give the handlers different priorities and accept ordering between them.
- The implementation is the same loop as `SequentialEventDispatcher` with a sort in front, including the same cancellation filter.

## Alternatives Considered

- **Keep parallel execution within a priority group and weaken the interface documentation** to "no guaranteed order". Rejected: it changes a public extensibility contract, and it keeps concurrent execution in a dispatcher that users pick specifically for ordering. Users who want concurrency already have `ParallelEventDispatcher`.
- **Run each group in parallel but record results in registration order.** Rejected: the order of side effects would still be undefined, and handler side effects are what the contract is about.

## Related Decisions

- [Extensibility Interface Evolution Before 1.0](./2026-09-24-extensibility-interface-evolution-pre-1-0.md): governs how this behavior change is labelled in commits (`fix:`, no breaking-change marker).
