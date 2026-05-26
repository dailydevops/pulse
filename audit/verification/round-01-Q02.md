# Q02 Verification

**Status:** CONFIRMED

**Evidence:**
- `src/NetEvolve.Pulse.PostgreSql/Scripts/OutboxMessage.sql:78-105` — `get_pending_outbox_messages` flips `Status` from 0 (Pending) to 1 (Processing) in a single `UPDATE ... RETURNING` with `FOR UPDATE SKIP LOCKED`.
- `src/NetEvolve.Pulse.PostgreSql/Scripts/OutboxMessage.sql:131-162` — `get_failed_outbox_messages_for_retry` also flips `Status` to 1 (Processing), and only filters on `Status=3` (Failed).
- `src/NetEvolve.Pulse.PostgreSql/Scripts/OutboxMessage.sql:180-183, 202-203` — `mark_outbox_message_completed` and `mark_outbox_message_failed` only update rows with `Status=1`; nothing rescues stuck rows.
- `src/NetEvolve.Pulse.SqlServer/Outbox/SqlServerOutboxRepository.cs:180-199` — `GetPendingAsync` calls a stored procedure that flips status to Processing.
- `src/NetEvolve.Pulse/Outbox/OutboxProcessorHostedService.cs:236-292` — `ProcessBatchAsync` calls `GetPendingAsync` + `GetFailedForRetryAsync`. Neither queries for stuck-Processing rows, and no reaper method is invoked.
- `src/NetEvolve.Pulse.Extensibility/Outbox/IOutboxRepository.cs:19-202` — interface contains no reaper / reclaim-stuck-processing method. There is no public surface a fix could rely on.

**Reasoning:**
The outbox follows a two-step lease pattern: claim (Pending -> Processing) and finalize (Processing -> Completed/Failed/DeadLetter). If the host crashes between those steps, the row sits in Processing forever because (a) `GetPendingAsync` filters `Status=0` only, (b) `GetFailedForRetryAsync` filters `Status=3` only, (c) `mark_outbox_message_*` SQL requires `Status=1` (so a manual SQL touch is the only escape), and (d) the `pulse.outbox.pending` gauge only counts `Status=0`, so monitoring is blind. This breaks the at-least-once delivery contract.

**Failing test (if confirmed):**
- Path: `tests/NetEvolve.Pulse.Tests.Unit/Outbox/StuckProcessingMessagesReaperTests.cs`
- Status: written

The test simulates a crash by directly setting a message to `OutboxMessageStatus.Processing` (the state a row is in after a successful `GetPendingAsync`). It then asserts that the public `IOutboxRepository` API provides some way to reclaim the message — there is none today, so the test fails at the assertion `Assert.That(reclaimMethodExists).IsTrue()`.

**Notes:**
The minimum exposure change required is a new method on `IOutboxRepository`, e.g.
```csharp
Task<int> ReclaimStuckProcessingAsync(TimeSpan stuckAfter, CancellationToken cancellationToken = default);
```
that flips rows whose `UpdatedAt < now - stuckAfter && Status = Processing` back to `Pending`, plus a corresponding tick inside `OutboxProcessorHostedService.ExecuteAsync` (every Nth poll). The current `Status` enum already supports the transition.
