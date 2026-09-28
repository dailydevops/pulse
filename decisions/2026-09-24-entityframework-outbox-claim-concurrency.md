---
authors:
  - Martin Stühmer

applyTo:
  - "src/NetEvolve.Pulse.EntityFramework/Outbox/*.cs"
  - "src/NetEvolve.Pulse.EntityFramework/Configurations/OutboxMessageConfigurationBase.cs"

created: 2026-09-24

lastModified: 2026-09-24

state: proposed

instructions: |
  MUST claim Entity Framework outbox messages in bulk executors with a single-table ExecuteUpdate over context.OutboxMessages filtered by the complete eligibility predicate (status, NextRetryAt, RetryCount) plus the candidate ids; MUST NOT run ExecuteUpdate on the ordered/limited candidate query.
  MUST keep both OutboxMessage.Status and OutboxMessage.UpdatedAt configured as concurrency tokens, and MUST write a new UpdatedAt on every status transition, so change-tracking executors reject stale claims.
  MUST NOT rely on provider-specific locking SQL (FOR UPDATE SKIP LOCKED, UPDLOCK/READPAST) in the provider-agnostic Entity Framework package.
---

# Decision: Entity Framework Outbox Claim Concurrency

Overlapping Entity Framework outbox pollers must never claim the same message. Bulk executors re-check the full eligibility predicate on the updated row itself, and change-tracking executors use `Status` and `UpdatedAt` as optimistic concurrency tokens.

## Context

`FetchAndMarkAsync` loads a batch of candidates and then claims it. The bulk executor used to run `ExecuteUpdateAsync` on the ordered and limited candidate query. EF Core cannot translate `OrderBy`/`Take` into a plain `UPDATE`, so it joined the target table back to a limited subquery. The eligibility predicate then existed only on that subquery.

PostgreSQL documents how a blocked `UPDATE` behaves under READ COMMITTED (the default isolation level) in [§13.2.1](https://www.postgresql.org/docs/current/transaction-iso.html#XACT-READ-COMMITTED):

> If the first updater commits, the second updater [...] will attempt to apply its operation to the updated version of the row. The search condition of the command (the `WHERE` clause) is re-evaluated to see if the updated version of the row still matches the search condition.

> [...] it can see the effects of concurrent updating commands on the same rows it is trying to update, but it does not see effects of those commands on other rows in the database. This behavior makes Read Committed mode unsuitable for commands that involve complex search conditions [...].

Only the primary-key join was re-evaluated on the target row, so a second poller overwrote the first poller's claim and both processed the whole batch. Integration tests confirmed the same duplicate claim on SQL Server with `READ_COMMITTED_SNAPSHOT` enabled.

EF Core does not help here on its own. The [ExecuteUpdate documentation](https://learn.microsoft.com/ef/core/saving/execute-insert-update-delete#concurrency-control-and-rows-affected) says:

> Since `ExecuteUpdate` and `ExecuteDelete` do not interact with the change tracker, they cannot automatically apply concurrency control.

The change-tracking executors, used for the EF InMemory provider and Oracle's MySQL provider, used `Status` as their only concurrency token. That left an ABA gap. A row could go `Failed → Processing → Failed` with a future `NextRetryAt` between a stale poller's load and its save. The stale poller's `WHERE Status = Failed` check still matched, and it claimed the row again.

## Decision

- The repository defines each claim's eligibility predicate once, as an expression. It builds the candidate query from that predicate (`Where(filter).OrderBy(...).Take(n)`) and passes the predicate to the executor.
- The bulk executor claims with `context.OutboxMessages.Where(filter).Where(m => ids.Contains(m.Id)).ExecuteUpdateAsync(...)`. This produces a single-table `UPDATE` whose `WHERE` clause holds the complete predicate. A blocked poller therefore re-checks status, retry schedule and retry count against the committed row. The affected-row count then covers only rows this caller transitioned, which keeps the uncontended fast path safe.
- `UpdatedAt` becomes a second concurrency token next to `Status`. Every transition writes `UpdatedAt`, so a change-tracking save rejects any row that changed after it was loaded, including ABA round trips.

## Consequences

- Duplicate claims across overlapping pollers are prevented on PostgreSQL, SQL Server (with or without RCSI), SQLite, MySQL and InMemory. Shared integration tests cover this in `EntityFrameworkOutboxClaimRaceTestsBase`.
- The schema does not change. `UpdatedAt` now appears in the `WHERE` clause of change-tracking `UPDATE`/`DELETE` statements for outbox rows. Applications that use EF Core migrations will see `IsConcurrencyToken()` on `UpdatedAt` in their next model snapshot. It produces no migration operation.
- `UpdatedAt` still serves as the claim token on the partial-claim path. Two claimers that write an identical timestamp, for example with a shared fake clock, cannot be told apart there. A dedicated claim-token column would remove that limit, at the cost of a schema change.
- Blocked pollers still wait on each other's row locks and do not skip them. That keeps behavior correct, but contention is higher than with lock skipping.

## Alternatives Considered

- **`SELECT ... FOR UPDATE SKIP LOCKED`** (PostgreSQL, MySQL 8) or `UPDLOCK, READPAST` hints (SQL Server). PostgreSQL [describes](https://www.postgresql.org/docs/current/sql-select.html#SQL-FOR-UPDATE-SHARE) `SKIP LOCKED` as follows: "any selected rows that cannot be immediately locked are skipped", and it "can be used to avoid lock contention with multiple consumers accessing a queue-like table". This is the best fit for throughput, but EF Core LINQ cannot express it. It would need provider-specific raw SQL and table and column name mapping inside a provider-agnostic package. The ADO.NET provider packages remain the place for this.
- **Re-checking only `Status` in the claim.** This is not enough, because it leaves the `Failed → Processing → Failed` ABA gap open.
- **A dedicated claim-token column.** This is the most robust way to identify one's own claim, but it requires a schema change for every existing installation. It was deferred.
- **`RetryCount` as the extra concurrency token.** This covers only the failure path. `UpdatedAt` covers every transition.

## Related Decisions (Optional)

- [DateTimeOffset and TimeProvider Usage](./2026-01-21-datetimeoffset-and-timeprovider-usage.md) - `UpdatedAt` comes from `TimeProvider`, which makes it usable as a concurrency token.
- [Outbox Unresolvable Event Types](./2026-09-24-outbox-unresolvable-event-types.md) - Unresolvable messages are dead-lettered only after the claim this decision protects.
