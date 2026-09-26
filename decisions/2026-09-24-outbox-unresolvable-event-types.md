---
authors:
  - Martin Stühmer

applyTo:
  - "src/NetEvolve.Pulse.Extensibility/Outbox/OutboxEventTypeResolver.cs"
  - "src/**/Outbox/*OutboxRepository*.cs"
  - "src/**/Outbox/*OutboxManagement*.cs"
  - "src/NetEvolve.Pulse.MongoDB/Outbox/OutboxDocumentMapper.cs"
  - "src/NetEvolve.Pulse.CosmosDb/Outbox/CosmosDbOutboxDocument.cs"
  - "src/NetEvolve.Pulse.EntityFramework/Configurations/TypeValueConverter.cs"

created: 2026-09-24

lastModified: 2026-09-24

state: proposed

instructions: |
  MUST rehydrate persisted outbox event type names only through OutboxEventTypeResolver.Resolve; MUST NOT call Type.GetType directly or throw for an unresolvable event type in a provider.
  MUST pass every batch returned by IOutboxRepository.GetPendingAsync and GetFailedForRetryAsync through DeadLetterUnresolvableAsync after the claim is committed, so an unresolvable event type dead-letters that message instead of failing the fetch.
  MUST return unresolvable messages from IOutboxManagement reads as the placeholder type, which carries the stored name, instead of skipping them.
---

# Outbox Unresolvable Event Types

An outbox message whose persisted event type name cannot be resolved is dead-lettered on fetch with an error that names the stored type. Management reads list it through a placeholder type that carries the stored name.

## Context

The outbox providers persist the assembly-qualified event type name and rehydrate it with `Type.GetType`. A name cannot be resolved when the type was renamed or removed, or when it is not compiled into a trimmed or NativeAOT outbox processor. Issue #772 documents that the behavior differed by provider:

* SQL Server, PostgreSQL, MySQL and SQLite threw an `InvalidOperationException` inside the read loop, so the whole fetch failed and every other message in the batch was blocked (poison batch).
* MongoDB and Entity Framework Core threw during mapping. The claimed rows stayed in `Processing` and were reclaimed and failed again on every lease expiry.
* Cosmos DB fell back to `object` and handed the message to the processor.

Management and inspector reads failed the same way, so an operator could not even list the message that blocked the outbox.

The Entity Framework Core provider materializes `OutboxMessage.EventType` through a value converter. A converter cannot skip a row, so throwing there always fails the whole query.

## Decision

* Add the public static class `OutboxEventTypeResolver` to `NetEvolve.Pulse.Extensibility`, next to `TypeExtensions.ToOutboxEventTypeName`:
  - `Resolve(string)` resolves and caches successful lookups. For a name that cannot be resolved, it returns a placeholder `Type` (a private `TypeDelegator` subclass). The placeholder reports the stored name as its `AssemblyQualifiedName`, so `ToOutboxEventTypeName` and the Entity Framework Core converter write the stored name back unchanged.
  - `IsUnresolvable(Type)` identifies the placeholder.
  - `DeadLetterUnresolvableAsync(IOutboxRepository, IReadOnlyList<OutboxMessage>, CancellationToken)` moves every placeholder message to `DeadLetter` through the repository's own `MarkAsDeadLetterAsync`. The error is `Cannot resolve event type '<stored name>'. ...`. It returns the remaining messages in their original order.
* All seven outbox providers (SQL Server, PostgreSQL, MySQL, SQLite, MongoDB, Cosmos DB and Entity Framework Core) use `Resolve` at every rehydration site. This replaces the per-provider `Type.GetType` calls, caches and `IL2057` suppressions as well as the Cosmos DB `object` fallback.
* `GetPendingAsync` and `GetFailedForRetryAsync` call `DeadLetterUnresolvableAsync` after the claim is committed. The SQLite claim holds a `BEGIN IMMEDIATE` write lock and the MySQL claim holds row locks, so dead-lettering inside the claim transaction would block or deadlock.
* Dead-lettering is the right terminal state: the message can never be delivered by this application, so retries only waste attempts. A replay from the inspector resets the message to `Pending` and, once the type is available again, it is delivered normally.
* `IOutboxManagement` reads return unresolvable messages with the placeholder instead of skipping them. The inspector therefore shows the stored type name and can replay or dismiss the message.
* The repositories do not log the dead-lettering, because none of them has a logger. The persisted error and the dead-letter statistics make the message visible.

## Consequences

* An unresolvable event type no longer blocks the other messages of a batch in any provider.
* The behavior is consistent across providers and covered by shared integration tests in `OutboxTestsBase`.
* `GetPendingAsync` and `GetFailedForRetryAsync` may return fewer messages than claimed.
* If dead-lettering is interrupted after the claim, the message stays in `Processing` and is reclaimed after the processing lease expires by the providers that reclaim expired leases.
* The placeholder compares equal only to placeholders with the same stored name, not to `typeof(object)`.
* `NetEvolve.Pulse.Extensibility` gains a public static class. External implementers of `IOutboxRepository` SHOULD use it in the same way. No interface changes.

## Alternatives Considered

* **Throw as before**: Rejected. One bad row blocks the whole outbox.
* **Skip the row and leave it claimed**: Rejected. The row would be reclaimed and skipped again on every lease expiry and never reach a terminal state.
* **Mark the row as `Failed`**: Rejected. The message can never succeed, so retries only consume attempts before it reaches `DeadLetter` anyway.
* **Fall back to `typeof(object)` everywhere, as Cosmos DB did**: Rejected. It loses the stored name, and the processor would try to deliver the message.
* **Dead-letter in `OutboxProcessorHostedService`**: The processor has a logger and would cover external repositories too. Rejected, because the repository contract would still return undeliverable messages, and the placeholder would have to cross the processor's grouping and per-event-type option lookups.
* **Skip unresolvable rows in management reads**: Rejected. The inspector could not show or dismiss the message that caused the dead letter.

## Related Decisions

* [NativeAOT and Trim Compatibility for Pulse Packages](./2026-09-24-nativeaot-trim-compatibility.md) - Unresolvable event types are the expected failure mode of an outbox processor that does not root its event types.
* [Outbox Inspection and Dead-Letter Dismissal](./2026-09-24-outbox-inspection-and-dead-letter-dismissal.md) - Management reads list unresolvable messages so they can be dismissed.
