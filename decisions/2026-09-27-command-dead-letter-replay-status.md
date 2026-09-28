---
authors:
  - Martin Stühmer

applyTo:
  - "src/NetEvolve.Pulse.Extensibility/DeadLetter/*.cs"
  - "src/**/DeadLetter/*CommandDeadLetterManagement.cs"
  - "src/NetEvolve.Pulse.AspNetCore/DeadLetter/**/*.cs"

created: 2026-09-27

lastModified: 2026-09-28

state: accepted

instructions: |
  MUST reject ICommandDeadLetterManagement.ReplayAsync for Dismissed entries with CommandDeadLetterEntryDismissedException before changing the status or dispatching; the inspector endpoint MUST map it to 409 Conflict.
  MUST keep Resolved and Replaying entries replayable (deliberate re-run, recovery of stranded entries).
  MUST reset the entry to New with CancellationToken.None when deserialization or dispatch throws or is cancelled, then rethrow the original exception, even when the reset itself fails; MUST write Resolved with CancellationToken.None after a successful dispatch.
---

# Decision: Command Dead Letter Replay Status Rules

`ICommandDeadLetterManagement.ReplayAsync` rejects dismissed entries and never leaves an entry in `Replaying` after a failed or cancelled dispatch. All five providers (Entity Framework, SQL Server, PostgreSQL, MySQL, SQLite) follow the same rules.

## Context

Issue #788 describes two status bugs that every provider shared:

- `ReplayAsync` did not check the status. A `Dismissed` entry could be replayed, although `DismissAsync` is documented as "preventing further replay attempts".
- The status was set to `Replaying` before dispatch, without a try/catch. When the handler threw or the call was cancelled, the entry stayed in `Replaying`. It no longer appeared in `GetPendingAsync` or the inspector's pending list.

## Decision

- Replaying a `Dismissed` entry throws `CommandDeadLetterEntryDismissedException`. The exception derives from `InvalidOperationException` and carries `EntryId`, like `CommandDeadLetterEntryNotFoundException`. The check runs before any write or dispatch.
- `MapCommandDeadLetterInspector` maps this exception, and only this one, to `409 Conflict`. An `InvalidOperationException` thrown by the replayed handler is not reported as a conflict.
- `Resolved` entries stay replayable, so an operator can deliberately re-run a command. `Replaying` entries stay replayable too, so entries stranded before this fix, or by a crashed process, can be recovered.
- When deserialization or dispatch throws, or the call is cancelled, the provider resets the status to `New` and rethrows the original exception. The entry shows up in `GetPendingAsync` again. The reset runs with `CancellationToken.None`, because the caller's token is already cancelled when a cancellation caused the failure.
- When the reset itself fails, for example because the database is unreachable, the reset error is discarded and the original exception is still rethrown. The caller and the inspector see the real cause of the failure instead of a database error. The entry then stays in `Replaying`, which remains replayable.
- The Entity Framework provider discards the pending changes in the context before the reset: added entities are detached, modified and deleted entities are reverted to their original values. The replayed handler may share the same context, and its unsaved changes must neither be persisted nor make the reset fail. The change tracker is not cleared, because the context may be scoped and shared with the caller: entities the caller loaded before the replay stay tracked. Changes the caller made before the replay are already saved together with the `Replaying` status, so every pending change belongs to the handler.
- The reset always targets `New`, including a failed re-run of a `Resolved` entry. The failure is visible in the pending list instead of being hidden as `Resolved`.
- After a successful dispatch, `Resolved` is written with `CancellationToken.None`. The command has already run, so a late cancellation must not leave the entry in `Replaying`.

## Consequences

- Dismissed commands are never re-executed through the management API or the inspector.
- A failed replay leaves no entry out of sight. Operators can retry it from the pending list.
- Failure details of the replay (exception type and message, attempt count, time) are recorded on the entry, as defined in [Command Dead Letter Replay Attempt Tracking](./2026-09-27-command-dead-letter-replay-attempt-tracking.md).
- The status check and the `Replaying` write are separate statements, so a concurrent `DismissAsync` between them is not detected. This is acceptable for an administrative operation.
- When the command dead letter interceptor (`AddCommandDeadLetter`) is registered, a failed replay does not store a new dead letter entry. See [Command Dead Letter Replay Attempt Tracking](./2026-09-27-command-dead-letter-replay-attempt-tracking.md).

## Alternatives Considered

- **Reset to the previous status:** A failed re-run of a `Resolved` entry would go back to `Resolved` and stay out of `GetPendingAsync`. Rejected for that reason.
- **Record a `Failed` status:** `CommandDeadLetterStatus` has no such value. Adding one changes statistics, all provider queries and the inspector. Rejected as out of scope.
- **Reject `Resolved` entries too:** Rejected, because a deliberate re-run is a valid operator action.
- **Reuse `InvalidOperationException` without a dedicated type:** Rejected, because the endpoint could not tell a dismissed entry apart from a failing handler.

## Related Decisions

- [Outbox Inspection and Dead-Letter Dismissal](./2026-09-24-outbox-inspection-and-dead-letter-dismissal.md) - Defines the comparable replay and dismissal semantics for the outbox.
- [Command Dead Letter Replay Attempt Tracking](./2026-09-27-command-dead-letter-replay-attempt-tracking.md) - Records failed replays on the replayed entry instead of adding a new one.
- [Extensibility Interface Evolution Pre-1.0](./2026-09-24-extensibility-interface-evolution-pre-1-0.md) - The documented behavior change of `ICommandDeadLetterManagement.ReplayAsync` affects external implementers.
