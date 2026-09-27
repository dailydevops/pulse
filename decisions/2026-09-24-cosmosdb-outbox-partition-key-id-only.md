---
authors:
  - Martin Stühmer

applyTo:
  - "src/NetEvolve.Pulse.CosmosDb/**/*.cs"
  - "src/NetEvolve.Pulse.CosmosDb/README.md"

created: 2026-09-24

lastModified: 2026-09-24

state: proposed

instructions: |
  The Cosmos DB outbox container MUST be partitioned on /id; CosmosDbOutboxOptions.PartitionKeyPath MUST only accept "/id" (ordinal) and MUST throw an ArgumentException naming the option when CosmosDbOutboxRepository or CosmosDbOutboxManagement is constructed with any other value.
  Point operations MUST keep using new PartitionKey(id); MUST NOT add a configurable partition key value without superseding this decision.
---

# Decision: Cosmos DB Outbox Supports Only the `/id` Partition Key Path

The Cosmos DB outbox provider supports only containers partitioned on `/id`. `CosmosDbOutboxOptions.PartitionKeyPath` keeps its default `/id`, and any other value is rejected when the repository or the management API is constructed.

## Context

Every point operation of `CosmosDbOutboxRepository` and `CosmosDbOutboxManagement` (claim patch, status patches, cleanup delete, get by id, replay, dismiss) passes `new PartitionKey(id)`. `PartitionKeyPath` was never read. On a container partitioned on another path, for example `/eventType`, inserts succeed because the SDK derives the key from the document, but every point operation returns `404 Not Found` ([Read an item](https://learn.microsoft.com/azure/cosmos-db/how-to-dotnet-read-item), [Troubleshoot not found](https://learn.microsoft.com/azure/cosmos-db/troubleshoot-not-found)). The claim and cleanup logic treat that 404 as "document already deleted", so the outbox stalls without an error (issue #854).

## Decision

- `PartitionKeyPath` accepts only `/id` (ordinal comparison). Anything else, including `/Id`, `/id/`, empty or whitespace, throws an `ArgumentException` that names the option and the supported value.
- The check runs in the constructors of `CosmosDbOutboxRepository` and `CosmosDbOutboxManagement`, so it fires on first resolution of these scoped services.
- The XML documentation and the package README state that the container must be partitioned on `/id`.

## Consequences

- A misconfigured container fails loudly instead of stalling the outbox silently. The outbox processor logs the error on every poll cycle and storing an outbox message throws.
- Configurations that set `PartitionKeyPath` to another value purely as documentation, on an `/id` container, now throw and must remove the setting or set it to `/id`.
- Status queries keep fanning out to all physical partitions. TTL cleanup keeps the container small.

## Alternatives Considered

- **Honor the configured path.** The repository would have to read the partition key value from each document for every point operation and every query candidate, map arbitrary JSON paths (nested and [hierarchical keys](https://learn.microsoft.com/azure/cosmos-db/hierarchical-partition-keys)) to `PartitionKey` values, and forbid paths on mutable properties such as `status`, because [partition key values cannot change in place](https://learn.microsoft.com/azure/cosmos-db/partitioning#choose-a-partition-key). That is a larger redesign without a concrete requirement. `/id` is the documented good choice for write-heavy containers with point reads ([Use item ID as the partition key](https://learn.microsoft.com/azure/cosmos-db/partitioning#use-item-id-as-the-partition-key)).
- **Remove or obsolete the option.** Removing it breaks the public API; `[Obsolete]` produces warnings for every current user of the default. Both can follow in a later breaking release.

## Related Decisions (Optional)

- [Extensibility Interface Evolution Pre-1.0](./2026-09-24-extensibility-interface-evolution-pre-1-0.md) - Governs how public API changes are versioned before 1.0.
