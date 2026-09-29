# NetEvolve.Pulse.CosmosDb

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.CosmosDb.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.CosmosDb/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.CosmosDb.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.CosmosDb/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

Azure Cosmos DB persistence provider for the Pulse outbox pattern using the official `Microsoft.Azure.Cosmos` SDK. Designed for document-oriented architectures on Azure Cosmos DB that need the outbox pattern without a relational database.

## Features

- Native Cosmos DB implementation of `IOutboxRepository` using `Microsoft.Azure.Cosmos`
- Optimistic concurrency via ETag-based conditional patch operations, so concurrent workers never claim the same message twice
- Dead-letter inspection, replay, dismissal and statistics via `IOutboxManagement`
- Optional TTL-based automatic cleanup of completed and dead-letter documents
- Configurable database and container name (default container: `outbox_messages`)
- Health check support via a container read
- Requires `CosmosClient` to be registered in the dependency injection container by the caller

## Installation

### NuGet Package Manager

```powershell
Install-Package NetEvolve.Pulse.CosmosDb
```

### .NET CLI

```bash
dotnet add package NetEvolve.Pulse.CosmosDb
```

### PackageReference

```xml
<PackageReference Include="NetEvolve.Pulse.CosmosDb" Version="x.x.x" />
```

## Container Setup

The provider does not create the database or the container. Create both before the application starts processing the outbox.

> [!IMPORTANT]
> The container **must** use the partition key path `/id`. The repository and the management API always use the document `id` (the outbox message ID) as partition key value for point reads, patches and deletes. On a container with any other partition key path these operations return `404 Not Found`, which would stall the outbox silently. `CosmosDbOutboxOptions.PartitionKeyPath` therefore only accepts `/id`; any other value fails options validation at host startup with an `OptionsValidationException`.

```csharp
using Microsoft.Azure.Cosmos;

var database = await cosmosClient.CreateDatabaseIfNotExistsAsync("MyDatabase");

_ = await database.Database.CreateContainerIfNotExistsAsync(
    new ContainerProperties(id: "outbox_messages", partitionKeyPath: "/id")
    {
        // Required when EnableTimeToLive is true: -1 turns TTL on without a default
        // expiry. The provider writes "ttl": -1 on pending, processing, failed and
        // replayed documents and "ttl": TtlSeconds on completed and dead-letter ones.
        // A positive default expires documents without a "ttl" property, for example
        // pending documents written by an older version, and loses those messages.
        DefaultTimeToLive = -1,
    });
```

Keep the default indexing policy (all paths indexed). The provider filters on `status`, `retryCount`, `nextRetryAt` and `updatedAt` and sorts by `_ts` and `updatedAt`.

With the `/id` partition key every document is its own logical partition. Point operations stay single-partition, but the recurring status queries (polling, retry polling, counts and cleanup) fan out to all physical partitions. Enable TTL cleanup to keep the container small and the fan-out inexpensive.

## Quick Start

```csharp
using Microsoft.Azure.Cosmos;
using NetEvolve.Pulse;

// Register CosmosClient in DI (required by the caller)
services.AddSingleton(new CosmosClient(connectionString));

services.AddPulse(config => config
    .AddCosmosDbOutbox(opts =>
    {
        opts.DatabaseName = "MyDatabase";
        opts.ContainerName = "outbox_messages";
    }));
```

## Usage

### Using `AddCosmosDbOutbox` (Full Setup)

Registers the core outbox services, the Cosmos DB repository and the management API in a single call. Calling `AddOutbox()` before is optional.

```csharp
services.AddSingleton(new CosmosClient(connectionString));

services.AddPulse(config => config
    .AddCosmosDbOutbox(opts =>
    {
        opts.DatabaseName = "MyDatabase";
        opts.EnableTimeToLive = true;
        opts.TtlSeconds = 3600; // remove completed and dead-letter documents after one hour
    }));
```

### Using `UseCosmosDbOutbox` (Provider Swap)

Call `AddOutbox()` first, then `UseCosmosDbOutbox` to replace any previously registered `IOutboxRepository` and `IOutboxManagement` with the Cosmos DB implementations.

```csharp
services.AddSingleton(new CosmosClient(connectionString));

services.AddPulse(config => config
    .AddOutbox()
    .UseCosmosDbOutbox(opts =>
    {
        opts.DatabaseName = "MyDatabase";
        opts.ContainerName = "outbox_messages";
    }));
```

### Registered Services

| Service | Implementation | Lifetime |
|---|---|---|
| `IEventOutbox` | `OutboxEventStore` | Scoped (`AddCosmosDbOutbox` only) |
| `IOutboxRepository` | `CosmosDbOutboxRepository` | Scoped |
| `IOutboxManagement` | `CosmosDbOutboxManagement` | Scoped |
| `TimeProvider` | `TimeProvider.System` | Singleton (if not already registered) |

## Configuration

| Property | Type | Default | Description |
|---|---|---|---|
| `DatabaseName` | `string` | _(required)_ | The Cosmos DB database name. The database must exist. |
| `ContainerName` | `string` | `outbox_messages` | The Cosmos DB container name. The container must exist. |
| `PartitionKeyPath` | `string` | `/id` | Only `/id` is supported; other values fail validation at startup. The container must use `/id` (see [Container Setup](#container-setup)). |
| `EnableTimeToLive` | `bool` | `false` | Sets the `ttl` property on documents that become `Completed` or `DeadLetter`, so the Cosmos DB TTL engine deletes them. All other documents (pending, processing, failed and replayed) get `ttl = -1`, so they never expire. Pending documents written by older versions have no `ttl` until they are claimed, so the container still requires `DefaultTimeToLive = -1`. |
| `TtlSeconds` | `int` | `86400` (24 hours) | TTL in seconds for completed and dead-letter documents. Only applies when `EnableTimeToLive` is `true`. |
| `ProcessingLeaseTimeout` | `TimeSpan` | 5 minutes | How long a claimed message may stay in `Processing` before the next pending poll reclaims it, for example after a crash or shutdown. Must be greater than zero; other values fail validation at startup. |

## Concurrency

Workers claim pending and retryable messages with a conditional patch (`IfMatchEtag`) that uses the `_etag` returned by the candidate query. When another worker changed the document in the meantime, Cosmos DB answers with `412 Precondition Failed` and the message is skipped, so each message is claimed by one worker only. Replay and dismissal in `IOutboxManagement` use the same ETag precondition.

Cosmos DB has no transactions across containers or with other data stores, so the outbox write is not atomic with your domain writes. This provider does not support `IOutboxTransactionScope`.

## Dead Letter Management

The `IOutboxManagement` service is registered automatically by `AddCosmosDbOutbox(...)` and `UseCosmosDbOutbox(...)`.

```csharp
using NetEvolve.Pulse.Extensibility.Outbox;

public class OutboxMonitorService
{
    private readonly IOutboxManagement _management;

    public OutboxMonitorService(IOutboxManagement management) => _management = management;

    public async Task PrintStatisticsAsync(CancellationToken ct)
    {
        var stats = await _management.GetStatisticsAsync(ct);
        Console.WriteLine($"Pending: {stats.Pending}, Dead Letter: {stats.DeadLetter}, Total: {stats.Total}");
    }

    public async Task ReplayAllDeadLettersAsync(CancellationToken ct)
    {
        var replayed = await _management.ReplayAllDeadLetterAsync(ct);
        Console.WriteLine($"Replayed {replayed} dead-letter messages.");
    }
}
```

### Available Operations

| Method | Description |
|---|---|
| `GetStatisticsAsync()` | Returns message counts grouped by status (`OutboxStatistics`) |
| `GetDeadLetterMessagesAsync(pageSize, page)` | Returns a paginated list of dead-letter messages |
| `GetDeadLetterMessageAsync(messageId)` | Returns a single dead-letter message by ID |
| `GetDeadLetterCountAsync()` | Returns the total count of dead-letter messages |
| `ReplayMessageAsync(messageId)` | Resets a single dead-letter message to Pending for reprocessing |
| `ReplayAllDeadLetterAsync()` | Resets all dead-letter messages to Pending and returns the updated count |
| `GetMessagesAsync(pageSize, page, status)` | Returns a paginated, read-only list of messages in any status, optionally filtered by status |
| `GetMessageAsync(messageId)` | Returns a single message by ID, regardless of its status |
| `DismissMessageAsync(messageId)` | Permanently deletes a single dead-letter message and returns whether one was deleted |

### Known Limitation: Paging Order

`GetDeadLetterMessagesAsync` and `GetMessagesAsync` sort by `updatedAt` (descending) only. A secondary sort by `id` would require a composite index that containers created for earlier versions do not have. Messages that share the same `updatedAt` value therefore have no guaranteed relative order and can shift between pages, so a message can appear on two pages or on none while you page through the results.

## NativeAOT and Trimming

The package is built with `IsAotCompatible` and has no trim or AOT warnings of its own. The `Microsoft.Azure.Cosmos` SDK and its default Newtonsoft.Json-based serializer are outside the control of this package; publish your application with NativeAOT and check the warnings reported for the SDK before you rely on it in production.

See [NativeAOT and Trimming](https://github.com/dailydevops/pulse/blob/main/src/NetEvolve.Pulse/README.md#nativeaot-and-trimming) in the `NetEvolve.Pulse` README for the payload serialization setup.

## Requirements

- .NET 8.0 or higher (net8.0, net9.0, net10.0 targets)
- An Azure Cosmos DB for NoSQL account (or the Cosmos DB emulator)
- `CosmosClient` registered in the dependency injection container
- An existing database and a container with partition key path `/id`

## Related Packages

- [**NetEvolve.Pulse**](https://www.nuget.org/packages/NetEvolve.Pulse/) – Core Pulse mediator and abstractions
- [**NetEvolve.Pulse.MongoDB**](https://www.nuget.org/packages/NetEvolve.Pulse.MongoDB/) – MongoDB outbox provider
- [**NetEvolve.Pulse.SqlServer**](https://www.nuget.org/packages/NetEvolve.Pulse.SqlServer/) – SQL Server outbox provider
- [**NetEvolve.Pulse.PostgreSql**](https://www.nuget.org/packages/NetEvolve.Pulse.PostgreSql/) – PostgreSQL outbox provider

## Documentation

For complete documentation, please visit the [official documentation](https://github.com/dailydevops/pulse/blob/main/README.md).

## Contributing

Contributions are welcome! Please read the [Contributing Guidelines](https://github.com/dailydevops/pulse/blob/main/CONTRIBUTING.md) before submitting a pull request.

## Support

- **Issues**: Report bugs or request features on [GitHub Issues](https://github.com/dailydevops/pulse/issues)
- **Documentation**: Read the full documentation at [https://github.com/dailydevops/pulse](https://github.com/dailydevops/pulse)

## License

This project is licensed under the MIT License - see the [LICENSE](https://github.com/dailydevops/pulse/blob/main/LICENSE) file for details.

---

> [!NOTE]
> **Made with ❤️ by the NetEvolve Team**
> Visit us at [https://www.daily-devops.net](https://www.daily-devops.net) for more information about our services and solutions.
