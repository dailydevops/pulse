---
authors:
  - Martin Stühmer

applyTo:
  - "src/NetEvolve.Pulse.Extensibility/Outbox/IOutboxRepository.cs"
  - "src/NetEvolve.Pulse.*/Outbox/*OutboxRepository*.cs"
  - "src/NetEvolve.Pulse.*/Scripts/OutboxMessage.sql"

created: 2026-09-28

lastModified: 2026-09-29

state: accepted

instructions: |
  MUST change an outbox message in every IOutboxRepository MarkAsCompleted/MarkAsFailed/MarkAsDeadLetter implementation (single and batch overloads) only while its status is Processing, as part of the same atomic storage operation (WHERE Status = 1, a status filter, or a conditional patch).
  MUST treat a Mark* call for a message in any other status, or for an unknown message, as a silent no-op; MUST NOT throw for it.
---

# Decision: Outbox Mark* Status Guard

Every outbox repository changes a message in `MarkAsCompletedAsync`, `MarkAsFailedAsync` and `MarkAsDeadLetterAsync` only while the message is still `Processing`. A call for a message in any other status is a silent no-op.

## Context

Several providers reclaim messages whose processing lease expired (`OutboxOptions.ProcessingLeaseTimeout`). The lease keeps delivery at-least-once when a worker crashes. It also allows the following race:

1. Worker A claims message M and stalls longer than the lease.
2. Worker B reclaims M, delivers it and marks it `Completed`.
3. Worker A resumes, its dispatch fails, and it calls `MarkAsFailedAsync(M)`.

Without a status guard, step 3 moves M back to `Failed` with `RetryCount + 1`, and the retry poll delivers it once more. A late `MarkAsDeadLetterAsync` moves an already delivered message into the dead-letter state, where a replay sends it again.

SQL Server, PostgreSQL and the Entity Framework single-message paths already filtered on `Status = Processing`. MongoDB, MySQL, SQLite, Cosmos DB and the Entity Framework batch paths filtered on the message id only. `IOutboxRepository` did not state the rule, so external implementers had no contract to follow.

## Decision

* Every `Mark*` implementation MUST include `Status = Processing` in the same atomic storage operation that changes the message:
  - Relational providers: `WHERE Id = @id AND Status = 1` (single) and `WHERE Status = 1 AND Id IN (...)` (batch).
  - MongoDB: a filter on `Id` and `Status`.
  - Cosmos DB: `PatchItemRequestOptions.FilterPredicate = "FROM c WHERE c.status = 1"`. A `412 Precondition Failed` response, or a `404 Not Found` with sub-status 0 (document missing), is a no-op; any other 404 (for example sub-status 1003, container or database missing) MUST still throw.
  - Entity Framework: the `Status == Processing` predicate in both `UpdateByQueryAsync` callers and `UpdateByIdsAsync` implementations.
* A `Mark*` call for a message in any other status, or for an unknown message, MUST be a silent no-op.
* The `IOutboxRepository` XML documentation states the rule for implementers.
* The shared integration tests in `OutboxTestsBase` verify the rule for every provider, for the single and the batch overloads.

## Consequences

* A late `Mark*` call can no longer overwrite a message that another worker has already completed, failed or dead-lettered. This removes the duplicate delivery and the dead-letter replay described above.
* The guard does not identify the owner of a claim. While worker B holds a reclaimed message in `Processing`, a late write from worker A still lands. Closing that window needs a claim token, which requires a schema change and a new `Mark*` signature.
* A `Mark*` call no longer changes a message that is not `Processing`, and it does not report that it skipped the message. Code that used `Mark*` to overwrite a settled message loses that behavior.
* External `IOutboxRepository` implementations SHOULD add the same guard. The interface signature does not change.

## Alternatives Considered

* **Claim token column.** It identifies the claim owner and closes the remaining window. It requires a schema change for every installation and an additional parameter on every `Mark*` method, so it is out of scope for this decision.
* **Compare `UpdatedAt` with the claim timestamp.** `Mark*` receives only the message id, so the caller cannot pass the claim timestamp without an interface change. It has the same cost as a claim token with weaker guarantees.
* **Throw when the guard does not match.** The processor cannot act on the exception, and SQL Server and PostgreSQL already treat the case as a no-op. A no-op keeps all providers consistent.

## Related Decisions

* [Entity Framework Outbox Claim Concurrency](./2026-09-28-entityframework-outbox-claim-concurrency.md) - Covers the claim side of the same outbox concurrency model; this decision covers the status transitions after the claim.
