# NetEvolve.Pulse.PostgreSql

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.PostgreSql.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.PostgreSql/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.PostgreSql.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.PostgreSql/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

PostgreSQL persistence provider for the Pulse outbox pattern using plain ADO.NET. Provides optimized PostgreSQL operations with proper transaction support and locking strategies for reliable event delivery in high-throughput scenarios.

## Features

- **Plain ADO.NET**: No ORM overhead, direct PostgreSQL access via `Npgsql`
- **Transaction Support**: Enlist outbox operations in existing `NpgsqlTransaction` instances
- **Optimized Queries**: Uses stored functions with `FOR UPDATE SKIP LOCKED` for concurrent access
- **Dead Letter Management**: Built-in support for inspecting, replaying, and monitoring dead-letter messages via `IOutboxManagement`
- **Configurable Schema**: Customize schema and table names for multi-tenant scenarios
- **Schema Interchangeability**: Uses canonical schema compatible with Entity Framework provider

## Installation

### NuGet Package Manager

```powershell
Install-Package NetEvolve.Pulse.PostgreSql
```

### .NET CLI

```bash
dotnet add package NetEvolve.Pulse.PostgreSql
```

### PackageReference

```xml
<PackageReference Include="NetEvolve.Pulse.PostgreSql" Version="x.x.x" />
```

## Database Setup

Before using this provider, execute the schema script to create the required database objects.

### Running the Script

**psql utility:**

```bash
psql -h your-host -d your-database -f OutboxMessage.sql
```

**pgAdmin or DBeaver:**

Open the script and execute it against your database.

### Schema Script Contents

The script creates:

- The configured schema (default: `pulse`)
- The `OutboxMessage` table with two optimized partial indexes

**Core functions:**

| Function | Purpose |
|---|---|
| `get_pending_outbox_messages` | Retrieves and locks pending messages for processing (FOR UPDATE SKIP LOCKED) |
| `get_failed_outbox_messages_for_retry` | Retrieves failed messages eligible for retry |
| `mark_outbox_message_completed` | Marks a message as successfully processed |
| `mark_outbox_message_failed` | Marks a message as failed with error details |
| `mark_outbox_message_dead_letter` | Moves a message to dead-letter status |
| `delete_completed_outbox_messages` | Removes old completed messages older than a given threshold |

**Management functions:**

| Function | Purpose |
|---|---|
| `get_dead_letter_outbox_messages` | Returns a paginated list of dead-letter messages |
| `get_dead_letter_outbox_message` | Returns a single dead-letter message by ID |
| `get_dead_letter_outbox_message_count` | Returns the total count of dead-letter messages |
| `replay_outbox_message` | Resets a single dead-letter message to Pending |
| `replay_all_dead_letter_outbox_messages` | Resets all dead-letter messages to Pending |
| `get_outbox_statistics` | Returns message counts grouped by status |

## Quick Start

```csharp
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse;

var services = new ServiceCollection();

services.AddPulse(config => config
    .AddOutbox(
        options => options.Schema = "pulse",
        processorOptions => processorOptions.BatchSize = 100)
    .AddPostgreSqlOutbox("Host=localhost;Database=MyDb;Username=postgres;Password=secret;")
);
```

### Using Configuration

```csharp
services.AddPulse(config => config
    .AddOutbox()
    .AddPostgreSqlOutbox(
        sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("Outbox")!,
        options =>
        {
            options.Schema = "messaging";
            options.TableName = "OutboxMessage";
        })
);
```

### Registered Services

`AddPostgreSqlOutbox(...)` registers the following services:

| Service | Implementation | Lifetime |
|---|---|---|
| `IOutboxRepository` | `PostgreSqlOutboxRepository` | Scoped |
| `IOutboxManagement` | `PostgreSqlOutboxManagement` | Scoped |
| `TimeProvider` | `TimeProvider.System` | Singleton (if not already registered) |

## Dead Letter Management

The `IOutboxManagement` service is automatically registered when calling `AddPostgreSqlOutbox(...)`. It provides operations for inspecting and recovering dead-letter messages, as well as monitoring outbox health.

```csharp
public class OutboxMonitorService
{
    private readonly IOutboxManagement _management;

    public OutboxMonitorService(IOutboxManagement management) =>
        _management = management;

    public async Task PrintStatisticsAsync(CancellationToken ct)
    {
        var stats = await _management.GetStatisticsAsync(ct);
        Console.WriteLine($"Pending: {stats.Pending}");
        Console.WriteLine($"Processing: {stats.Processing}");
        Console.WriteLine($"Completed: {stats.Completed}");
        Console.WriteLine($"Failed: {stats.Failed}");
        Console.WriteLine($"Dead Letter: {stats.DeadLetter}");
        Console.WriteLine($"Total: {stats.Total}");
    }

    public async Task ReplayAllDeadLettersAsync(CancellationToken ct)
    {
        var replayed = await _management.ReplayAllDeadLetterAsync(ct);
        Console.WriteLine($"Replayed {replayed} dead-letter messages.");
    }
}
```

## Transaction Integration

### Manual Transaction Enlistment

```csharp
public class OrderService
{
    private readonly string _connectionString;
    private readonly IServiceProvider _serviceProvider;

    public async Task CreateOrderAsync(CreateOrderRequest request, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        try
        {
            // Business operation
            await using var cmd = new NpgsqlCommand("INSERT INTO orders ...", connection, transaction);
            await cmd.ExecuteNonQueryAsync(ct);

            // Store event in outbox (same transaction)
            var outbox = new PostgreSqlEventOutbox(
                connection,
                _serviceProvider.GetRequiredService<IOptions<OutboxOptions>>(),
                TimeProvider.System,
                transaction);

            await outbox.StoreAsync(new OrderCreatedEvent { OrderId = orderId }, ct);

            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }
}
```

## Performance Considerations

### Indexing

The default schema includes optimized partial indexes for:

- Pending message polling (`Status`, `CreatedAt`) where Status in (0, 3)
- Completed message cleanup (`Status`, `ProcessedAt`) where Status = 2

### Stored Functions

Operations use stored functions with:

- `FOR UPDATE SKIP LOCKED` to skip locked rows during concurrent polling
- CTE-based atomic select-and-update for pending message retrieval

## Requirements

- .NET 8.0, .NET 9.0, or .NET 10.0
- PostgreSQL 12 or later
- `Npgsql` for database connectivity
- `Microsoft.Extensions.Hosting` for the background processor

## Related Packages

- [**NetEvolve.Pulse**](https://www.nuget.org/packages/NetEvolve.Pulse/) - Core mediator and outbox abstractions
- [**NetEvolve.Pulse.Dapr**](https://www.nuget.org/packages/NetEvolve.Pulse.Dapr/) - Dapr pub/sub integration for event dispatch
- [**NetEvolve.Pulse.Extensibility**](https://www.nuget.org/packages/NetEvolve.Pulse.Extensibility/) - Core contracts and abstractions
- [**NetEvolve.Pulse.EntityFramework**](https://www.nuget.org/packages/NetEvolve.Pulse.EntityFramework/) - Entity Framework persistence
- [**NetEvolve.Pulse.SqlServer**](https://www.nuget.org/packages/NetEvolve.Pulse.SqlServer/) - SQL Server ADO.NET persistence
- [**NetEvolve.Pulse.Polly**](https://www.nuget.org/packages/NetEvolve.Pulse.Polly/) - Polly v8 resilience policies integration

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
