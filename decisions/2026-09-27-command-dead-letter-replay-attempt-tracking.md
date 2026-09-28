---
authors:
  - Martin Stühmer

applyTo:
  - "src/NetEvolve.Pulse.Extensibility/DeadLetter/*.cs"
  - "src/**/DeadLetter/*CommandDeadLetterManagement.cs"
  - "src/NetEvolve.Pulse/Interceptors/CommandDeadLetterInterceptor.cs"

created: 2026-09-27

lastModified: 2026-09-28

state: accepted

instructions: |
  MUST record a failed or cancelled ICommandDeadLetterManagement.ReplayAsync on the replayed entry, in the same statement as the reset to New: AttemptCount + 1, ExceptionType and ExceptionMessage of the new exception, OccurredAt from the injected TimeProvider.
  MUST NOT store a new dead letter entry for the command instance that CommandDeadLetterReplayDispatcher is replaying; the interceptor checks CommandDeadLetterReplayDispatcher.IsReplayedCommand (reference equality) and still rethrows. Other commands sent by the replayed handler are stored as usual.
  MUST NOT bypass the idempotency reservation during a replay.
---

# Decision: Command Dead Letter Replay Attempt Tracking

A failed replay of a command dead letter entry updates that entry instead of adding a new one. The attempt count grows with every failed replay, and the entry always shows the latest failure. All five providers (Entity Framework, SQL Server, PostgreSQL, MySQL, SQLite) follow the same rules.

## Context

Issue #864 follows the [Command Dead Letter Replay Status Rules](./2026-09-27-command-dead-letter-replay-status.md). `ReplayAsync` dispatches the stored command through the normal mediator pipeline, which also contains `CommandDeadLetterInterceptor` when `AddCommandDeadLetter()` is registered. Nothing told the interceptor that a dispatch was a replay. So every failed replay stored a new entry with `AttemptCount = 1`, and the replayed entry was reset to `New` as well. After three failed replays, the pending list showed the same command four times. The replayed entry still showed `AttemptCount = 1` and the exception of the first failure.

## Decision

* `CommandDeadLetterReplayDispatcher.ReplayAsync` stores the deserialized command instance in an `AsyncLocal` for the duration of the dispatch and restores the previous value afterwards. The new public method `CommandDeadLetterReplayDispatcher.IsReplayedCommand(object)` compares a command with that instance by reference.
* `CommandDeadLetterInterceptor` does not call `ICommandDeadLetterStore.StoreAsync` when the failed request is the replayed command. It still rethrows the exception. The check compares references, so a command that the replayed handler sends is still recorded, even when it is equal to the replayed command by value.
* The reset to `New` after a failed or cancelled replay is one update of the entry. It increments `AttemptCount` by one, sets `ExceptionType` and `ExceptionMessage` from the new exception and sets `OccurredAt` to `TimeProvider.GetUtcNow()`. The ADO.NET providers do this with one `UPDATE ... SET AttemptCount = AttemptCount + 1`, so the count is never read and written back. The Entity Framework provider increments the tracked entity, like all its other status changes. If two replays of the same entry race past the status check, which the status decision accepts, it can lose one attempt. `ExceptionType` is truncated to `CommandDeadLetterSchema.MaxLengths.ExceptionType`, so a long generic exception type name cannot make the update fail and leave the entry in `Replaying`. The rules of the status decision still apply: the update runs with `CancellationToken.None`, and a failure of the update is discarded so the original exception is rethrown.
* Every command dead letter management takes a `TimeProvider` through its constructor. The `Add*CommandDeadLetterStore` extensions register `TimeProvider.System` with `TryAddSingleton`, as the idempotency extensions do.
* A successful replay does not change `AttemptCount`.
* The idempotency reservation is not bypassed during a replay. The failed attempt that created the entry has already reserved the key, whatever the registration order of `AddIdempotency()` and `AddCommandDeadLetter()`. A replay of an `IIdempotentCommand` therefore fails with `IdempotencyConflictException` until the key is removed from the idempotency store. That conflict is recorded on the replayed entry like any other failure. This is documented on `ICommandDeadLetterManagement.ReplayAsync`.

## Consequences

* Each failed command appears once in the pending list, with an accurate attempt count and its latest failure.
* `GetPendingAsync` orders entries by `OccurredAt`. A failed replay moves the entry to the end of the pending list.
* A cancelled replay counts as an attempt and records the `OperationCanceledException` as the latest failure. This matches the status decision, which handles cancellation on the same path.
* `CommandDeadLetterReplayDispatcher.IsReplayedCommand` is a new public API in `NetEvolve.Pulse.Extensibility`. The management constructors gain a `TimeProvider` parameter. They are internal and resolved through dependency injection, so only code that constructs them directly, such as tests, is affected.
* External `ICommandDeadLetterManagement` implementations have to record the attempt on the entry themselves. The interceptor skips the replayed command for every implementation that uses `CommandDeadLetterReplayDispatcher`.
* `ICommandDeadLetterStore` implementations still use `DateTimeOffset.UtcNow` for `OccurredAt` on insert. Moving them to `TimeProvider` is not part of this decision.

## Alternatives Considered

* **Let the interceptor update the existing entry:** The interceptor would need the entry id and an update method on `ICommandDeadLetterStore`. Rejected, because the management already owns the entry and resets it in the same code path.
* **Mark the whole replay flow instead of the command instance:** Commands that the replayed handler sends would no longer be recorded when they fail, although they are different commands. Rejected.
* **Compare the replayed command by value:** Commands are often records. An equal command sent by the handler would be skipped. Rejected.
* **Bypass or release the idempotency reservation during a replay:** When `AddCommandDeadLetter()` wraps `AddIdempotency()`, a rejected duplicate submission is itself stored as a dead letter entry. Bypassing the reservation on replay would run that duplicate. `IIdempotencyStore` has no operation to release a key either. Rejected.

## Related Decisions

* [Command Dead Letter Replay Status Rules](./2026-09-27-command-dead-letter-replay-status.md) - Defines the status rules this decision extends; its consequences on failure details and duplicate entries are resolved here.
* [DateTimeOffset and TimeProvider Usage](./2026-01-21-datetimeoffset-and-timeprovider-usage.md) - The replay failure timestamp comes from the injected `TimeProvider`.
* [Extensibility Interface Evolution Pre-1.0](./2026-09-24-extensibility-interface-evolution-pre-1-0.md) - The new public dispatcher method and the documented `ReplayAsync` behavior affect external implementers.
