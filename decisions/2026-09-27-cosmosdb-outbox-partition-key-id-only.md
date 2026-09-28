---
authors:
  - Martin Stühmer

applyTo:
  - "src/NetEvolve.Pulse.CosmosDb/**/*.cs"
  - "src/NetEvolve.Pulse.CosmosDb/README.md"

created: 2026-09-27

lastModified: 2026-09-28

state: accepted

instructions: |
  The Cosmos DB outbox container MUST be partitioned on /id; CosmosDbOutboxOptions.PartitionKeyPath MUST only accept "/id" (ordinal).
  Any other value MUST fail at host startup through CosmosDbOutboxOptionsValidator (IValidateOptions, registered with TryAddEnumerable and ValidateOnStart by AddCosmosDbOutbox and UseCosmosDbOutbox), with a message naming the option.
  CosmosDbOutboxRepository and CosmosDbOutboxManagement MUST keep throwing an ArgumentException for such a value when constructed, as a fallback.
  Point operations MUST keep using new PartitionKey(id); MUST NOT add a configurable partition key value without superseding this decision.
---

# Decision: Cosmos DB Outbox Supports Only the `/id` Partition Key Path

The Cosmos DB outbox provider supports only containers partitioned on `/id`. `CosmosDbOutboxOptions.PartitionKeyPath` keeps its default `/id`, and any other value is rejected at host startup.

## Context

Every point operation of `CosmosDbOutboxRepository` and `CosmosDbOutboxManagement` (claim patch, status patches, cleanup delete, get by id, replay, dismiss) passes `new PartitionKey(id)`. `PartitionKeyPath` was never read. On a container partitioned on another path, for example `/eventType`, inserts succeed because the SDK derives the key from the document, but every point operation returns `404 Not Found` ([Read an item](https://learn.microsoft.com/azure/cosmos-db/how-to-dotnet-read-item), [Troubleshoot not found](https://learn.microsoft.com/azure/cosmos-db/troubleshoot-not-found)). The claim and cleanup logic treat that 404 as "document already deleted", so the outbox stalls without an error (issue #854).

## Decision

- `PartitionKeyPath` accepts only `/id` (ordinal comparison). Anything else, including `/Id`, `/id/`, empty or whitespace, is rejected with a message that names the option and the supported value.
- The check runs at host startup: `AddCosmosDbOutbox` and `UseCosmosDbOutbox` register an internal `CosmosDbOutboxOptionsValidator` (`IValidateOptions<CosmosDbOutboxOptions>`) with `TryAddEnumerable` and call `ValidateOnStart()`, as the Azure Queue Storage, Azure Service Bus, Dapr, RabbitMQ and Redis providers do. A bad value makes `IHost.StartAsync` throw an `OptionsValidationException`, and so does any later resolution of the options.
- The constructors of `CosmosDbOutboxRepository` and `CosmosDbOutboxManagement` keep the same check and throw an `ArgumentException`, for options that do not go through the validator.
- The XML documentation and the package README state that the container must be partitioned on `/id`.

## Consequences

- A misconfigured `PartitionKeyPath` stops the host at startup instead of stalling the outbox silently.
- Configurations that set `PartitionKeyPath` to another value purely as documentation, on an `/id` container, now fail at startup and must remove the setting or set it to `/id`.
- The option value is checked, not the container. A container created with another partition key path while the option keeps `/id` still returns `404 Not Found` on point operations; the README and XML documentation state the container requirement.
- Status queries keep fanning out to all physical partitions. TTL cleanup keeps the container small.

## Alternatives Considered

- **Honor the configured path.** The repository would have to read the partition key value from each document for every point operation and every query candidate, map arbitrary JSON paths (nested and [hierarchical keys](https://learn.microsoft.com/azure/cosmos-db/hierarchical-partition-keys)) to `PartitionKey` values, and forbid paths on mutable properties such as `status`, because [partition key values cannot change in place](https://learn.microsoft.com/azure/cosmos-db/partitioning#choose-a-partition-key). That is a larger redesign without a concrete requirement. `/id` is the documented good choice for write-heavy containers with point reads ([Use item ID as the partition key](https://learn.microsoft.com/azure/cosmos-db/partitioning#use-item-id-as-the-partition-key)).
- **Remove or obsolete the option.** Removing it breaks the public API; `[Obsolete]` produces warnings for every current user of the default. Both can follow in a later breaking release.

## Related Decisions (Optional)

- [Extensibility Interface Evolution Pre-1.0](./2026-09-24-extensibility-interface-evolution-pre-1-0.md) - Governs how public API changes are versioned before 1.0.
