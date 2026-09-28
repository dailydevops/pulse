---
authors:
  - Martin Stühmer

applyTo:
  - "src/NetEvolve.Pulse.EntityFramework/Outbox/EntityFrameworkOutboxRepository*.cs"
  - "src/NetEvolve.Pulse.CosmosDb/Outbox/CosmosDbOutboxRepository.cs"
  - "src/NetEvolve.Pulse.CosmosDb/Outbox/CosmosDbOutboxOptions.cs"

created: 2026-09-28

lastModified: 2026-09-28

state: proposed

instructions: |
  MUST reclaim Processing outbox messages whose UpdatedAt is at or before now minus ProcessingLeaseTimeout in the Entity Framework Core and Cosmos DB GetPendingAsync, as part of the same claim predicate as Pending messages; MUST NOT add a lease column.
  MUST read the Entity Framework Core lease from OutboxOptions.ProcessingLeaseTimeout and the Cosmos DB lease from CosmosDbOutboxOptions.ProcessingLeaseTimeout, and MUST reject values that are not greater than TimeSpan.Zero.
  MUST return the Cosmos DB documents already claimed when a later claim patch fails with a CosmosException, and MUST rethrow when nothing was claimed yet.
---

# Decision: Entity Framework Core and Cosmos DB Outbox Lease Reclaim

The Entity Framework Core and Cosmos DB outbox repositories reclaim messages that stay in `Processing` longer than the processing lease. `UpdatedAt` serves as the lease timestamp. A Cosmos DB claim that fails part-way returns the documents it has already claimed.

## Context

The SQL Server, PostgreSQL, MySQL, SQLite and MongoDB repositories reclaim `Processing` messages whose lease has expired. The Entity Framework Core and Cosmos DB repositories selected only `Pending` messages in `GetPendingAsync`. A message stayed in `Processing` forever after a graceful shutdown mid-batch, a cancellation between send and completion, a failure after the claim committed, or a crashed host. `IOutboxManagement` replays and dismisses only dead-letter messages, so recovery needed a manual database edit (issue #813).

The Cosmos DB claim patches the candidates one at a time. A `429 Too Many Requests` or `503 Service Unavailable` on a later candidate threw away the documents that the earlier patches had already moved to `Processing`.

## Decision

- `GetPendingAsync` selects `(Status = Pending AND (NextRetryAt IS NULL OR NextRetryAt <= now)) OR (Status = Processing AND UpdatedAt <= now - ProcessingLeaseTimeout)` in both providers.
- Entity Framework Core passes this predicate as the claim filter. The bulk executor re-checks it in the claiming `UPDATE`, as required by [Entity Framework Outbox Claim Concurrency](./2026-09-28-entityframework-outbox-claim-concurrency.md). The reclaim writes a fresh `UpdatedAt`, so a competing poller no longer matches the row. The change-tracking executors reject the same race through the `Status` and `UpdatedAt` concurrency tokens.
- Cosmos DB keeps the `IfMatchEtag` claim, which rejects a second reclaim of the same document.
- Entity Framework Core reads `OutboxOptions.ProcessingLeaseTimeout`, like the ADO.NET providers. Cosmos DB reads the new `CosmosDbOutboxOptions.ProcessingLeaseTimeout`, like MongoDB reads `MongoDbOutboxOptions.ProcessingLeaseTimeout`, because Cosmos DB applications configure only `CosmosDbOutboxOptions`. Both default to 5 minutes, and both repositories reject values that are not greater than `TimeSpan.Zero`.
- When a Cosmos DB claim patch fails with a `CosmosException` other than `412 Precondition Failed` or `404 Not Found` after at least one document was claimed, the claim stops and returns the documents claimed so far. When nothing was claimed yet, the exception propagates. Cancellation always propagates.

## Consequences

- The Entity Framework Core and Cosmos DB outboxes keep at-least-once delivery after shutdowns, cancellations and crashes. The shared tests in `OutboxTestsBase` cover the reclaim and the active lease for every provider, and `EntityFrameworkOutboxClaimRaceTestsBase` covers overlapping reclaims.
- The schema does not change. Documents and rows that were stranded in `Processing` before the upgrade are reclaimed on the first poll after the upgrade.
- A message whose dispatch takes longer than the lease can be dispatched twice. This matches the other providers. Choose the lease comfortably larger than the longest dispatch.
- `UpdatedAt` also changes on every other status transition, so the lease starts at the claim. A claim that is not renewed during a long dispatch expires after the lease timeout.
- `GetPendingCountAsync` still counts only `Pending` messages. Expired `Processing` messages do not appear in the pending count until they are reclaimed.
- The Cosmos DB pending query orders by `_ts` and filters on `status`, `nextRetryAt` and `updatedAt`. It needs no composite index under the default indexing policy.
- `CosmosDbOutboxOptions` gains a public property. External implementers of `IOutboxRepository` are not affected.

## Alternatives Considered

- **A dedicated lease column (for example `ProcessingStartedAt`, as in MongoDB).** It separates the lease from other updates, but requires a schema change for every existing installation. `UpdatedAt` is written on every claim and already serves as the claim token.
- **Cosmos DB reading `OutboxOptions.ProcessingLeaseTimeout`.** This adds no public API, but `AddCosmosDbOutbox` users never configure `OutboxOptions`, so the setting would sit apart from the rest of the provider configuration.
- **Reclaiming in `GetFailedForRetryAsync`.** The other providers reclaim in the pending poll. Doing the same keeps the processor behavior identical across providers.
- **Retrying throttled Cosmos DB patches in the claim.** The SDK already retries throttled requests. Returning the claimed documents keeps the claim simple, and the next poll picks up the remaining candidates.

## Related Decisions (Optional)

- [Entity Framework Outbox Claim Concurrency](./2026-09-28-entityframework-outbox-claim-concurrency.md) - The widened claim predicate reaches the claiming `UPDATE` through the claim filter defined there.
- [Outbox Unresolvable Event Types](./2026-09-24-outbox-unresolvable-event-types.md) - Unresolvable messages left in `Processing` are now reclaimed by every provider.
- [DateTimeOffset and TimeProvider Usage](./2026-01-21-datetimeoffset-and-timeprovider-usage.md) - The lease cutoff comes from `TimeProvider`.
