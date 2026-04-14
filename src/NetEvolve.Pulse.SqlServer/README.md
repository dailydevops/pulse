# NetEvolve.Pulse.SqlServer

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.SqlServer.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.SqlServer/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.SqlServer.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.SqlServer/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

SQL Server persistence provider for the Pulse outbox pattern and idempotency store using plain ADO.NET. Provides optimized T-SQL operations with proper transaction support and locking strategies for reliable event delivery and at-most-once command execution in high-throughput scenarios.

## Features

- **Plain ADO.NET**: No ORM overhead, direct SQL Server access via `Microsoft.Data.SqlClient`
- **Outbox Pattern**: Reliable event delivery with transaction support
- **Idempotency Store**: At-most-once command execution without requiring EF Core
- **Transaction Support**: Enlist outbox operations in existing `SqlTransaction` instances
- **Optimized Queries**: Uses stored procedures with ROWLOCK/READPAST hints for concurrent access
- **Dead Letter Management**: Built-in support for inspecting, replaying, and monitoring dead-letter messages via `IOutboxManagement`
- **Configurable Schema**: Customize schema and table names for multi-tenant scenarios
- **Schema Interchangeability**: Uses canonical schema compatible with Entity Framework provider

## Installation

### NuGet Package Manager

```powershell
Install-Package NetEvolve.Pulse.SqlServer
```

### .NET CLI

```bash
dotnet add package NetEvolve.Pulse.SqlServer
```

### PackageReference

```xml
<PackageReference Include="NetEvolve.Pulse.SqlServer" Version="x.x.x" />
```

## Database Setup

Before using this provider, execute the schema script to create the required database objects.

> [!IMPORTANT]
> The script uses [SQLCMD variables](https://learn.microsoft.com/sql/tools/sqlcmd/sqlcmd-use-scripting-variables) (`:setvar`) and **must be run in SQLCMD mode**.

### Running the Script

**sqlcmd utility:**

```powershell
sqlcmd -S your-server -d your-database -i OutboxMessage.sql
```

**SQL Server Management Studio (SSMS):**

Enable SQLCMD Mode via `Query > SQLCMD Mode` (Ctrl+Shift+Q), then execute the script.

**Azure Data Studio:**

Enable SQLCMD via the query toolbar before executing.

### SQLCMD Variables

The script exposes the following configurable variables at the top of `OutboxMessage.sql`:

| Variable | Default | Description |
|---|---|---|
| `SchemaName` | `pulse` | Database schema name |
| `TableName` | `OutboxMessage` | Table name |

To use custom names, change the `:setvar` values before executing:

```sql
:setvar SchemaName "myapp"
:setvar TableName "Events"
```

### Schema Script Contents

The script creates:

- The configured schema (default: `[pulse]`)
- The `[OutboxMessage]` table with two optimized non-clustered indexes

**Core stored procedures:**

| Procedure | Purpose |
|---|---|
| `usp_GetPendingOutboxMessages` | Retrieves and locks pending messages for processing |
| `usp_GetFailedOutboxMessagesForRetry` | Retrieves failed messages eligible for retry |
| `usp_MarkOutboxMessageCompleted` | Marks a message as successfully processed |
| `usp_MarkOutboxMessageFailed` | Marks a message as failed with error details |
| `usp_MarkOutboxMessageDeadLetter` | Moves a message to dead-letter status |
| `usp_DeleteCompletedOutboxMessages` | Removes old completed messages older than a given threshold |

**Management stored procedures:**

| Procedure | Purpose |
|---|---|
| `usp_GetDeadLetterOutboxMessages` | Returns a paginated list of dead-letter messages |
| `usp_GetDeadLetterOutboxMessage` | Returns a single dead-letter message by ID |
| `usp_GetDeadLetterOutboxMessageCount` | Returns the total count of dead-letter messages |
| `usp_ReplayOutboxMessage` | Resets a single dead-letter message to Pending |
| `usp_ReplayAllDeadLetterOutboxMessages` | Resets all dead-letter messages to Pending |
| `usp_GetOutboxStatistics` | Returns message counts grouped by status |

## Quick Start

### Outbox Pattern

```csharp
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse;

var services = new ServiceCollection();

services.AddPulse(config => config
    .AddOutbox(
        options => options.Schema = "pulse",
        processorOptions => processorOptions.BatchSize = 100)
    .AddSqlServerOutbox("Server=.;Database=MyDb;Integrated Security=true;TrustServerCertificate=true;")
);
```

### Idempotency Store

The SQL Server idempotency store provides at-most-once command execution for applications using raw ADO.NET or Dapper, without requiring EF Core.

```csharp
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse;

var services = new ServiceCollection();

services.AddPulse(config => config
    .AddIdempotency()
    .AddSqlServerIdempotencyStore("Server=.;Database=MyDb;Integrated Security=true;TrustServerCertificate=true;")
);
```

#### Database Setup for Idempotency

Execute the `IdempotencyKey.sql` script from the `Scripts` folder (same SQLCMD requirements as `OutboxMessage.sql`):

```powershell
sqlcmd -S your-server -d your-database -i IdempotencyKey.sql
```

The script creates:
- The `[IdempotencyKey]` table with `IdempotencyKey` (PK) and `CreatedAt` columns
- Stored procedures: `usp_ExistsIdempotencyKey`, `usp_InsertIdempotencyKey`, `usp_DeleteExpiredIdempotencyKeys`

#### Using Idempotent Commands

```csharp
using NetEvolve.Pulse.Extensibility.Idempotency;

public sealed record CreateOrderCommand(string OrderId, decimal Amount) 
    : IIdempotentCommand<OrderCreatedResult>
{
    // The IdempotencyKey is checked before the handler executes
    public string IdempotencyKey => OrderId;
}

public sealed class CreateOrderCommandHandler 
    : ICommandHandler<CreateOrderCommand, OrderCreatedResult>
{
    public async Task<OrderCreatedResult> HandleAsync(
        CreateOrderCommand command, 
        CancellationToken cancellationToken)
    {
        // This handler will only execute once per unique OrderId
        // Subsequent requests with the same OrderId will throw IdempotencyConflictException
        return new OrderCreatedResult(command.OrderId);
    }
}
```

#### Time-To-Live Configuration

```csharp
services.AddPulse(config => config
    .AddIdempotency()
    .AddSqlServerIdempotencyStore(
        "Server=.;Database=MyDb;Integrated Security=true;",
        options =>
        {
            options.Schema = "pulse";
            options.TableName = "IdempotencyKey";
            options.TimeToLive = TimeSpan.FromHours(24); // Keys expire after 24 hours
        })
);
```

### Using Configuration

```csharp
services.AddPulse(config => config
    .AddOutbox()
    .AddSqlServerOutbox(
        sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("Outbox")!,
        options =>
        {
            options.Schema = "messaging";
            options.TableName = "OutboxMessage";
        })
);
```

### Registered Services

#### Outbox

`AddSqlServerOutbox(...)` registers the following services:

| Service | Implementation | Lifetime |
|---|---|---|
| `IOutboxRepository` | `SqlServerOutboxRepository` | Scoped |
| `IOutboxManagement` | `SqlServerOutboxManagement` | Scoped |
| `TimeProvider` | `TimeProvider.System` | Singleton (if not already registered) |

#### Idempotency Store

`AddSqlServerIdempotencyStore(...)` registers the following services:

| Service | Implementation | Lifetime |
|---|---|---|
| `IIdempotencyKeyRepository` | `SqlServerIdempotencyKeyRepository` | Scoped |
| `IIdempotencyStore` | `IdempotencyStore` | Scoped |
| `TimeProvider` | `TimeProvider.System` | Singleton (if not already registered) |

## Dead Letter Management

The `IOutboxManagement` service is automatically registered when calling `AddSqlServerOutbox(...)`. It provides operations for inspecting and recovering dead-letter messages, as well as monitoring outbox health.

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

### Available Operations

| Method | Description |
|---|---|
| `GetStatisticsAsync()` | Returns message counts grouped by status (`OutboxStatistics`) |
| `GetDeadLetterMessagesAsync(pageSize, page)` | Returns a paginated list of dead-letter messages |
| `GetDeadLetterMessageAsync(messageId)` | Returns a single dead-letter message by ID |
| `GetDeadLetterCountAsync()` | Returns the total count of dead-letter messages |
| `ReplayMessageAsync(messageId)` | Resets a single dead-letter message to Pending for reprocessing |
| `ReplayAllDeadLetterAsync()` | Resets all dead-letter messages to Pending and returns the updated count |

## Transaction Integration

### Manual Transaction Enlistment

```csharp
public class OrderService
{
    private readonly string _connectionString;
    private readonly IServiceProvider _serviceProvider;

    public async Task CreateOrderAsync(CreateOrderRequest request, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = connection.BeginTransaction();

        try
        {
            // Business operation
            await using var cmd = new SqlCommand("INSERT INTO [Order] ...", connection, transaction);
            await cmd.ExecuteNonQueryAsync(ct);

            // Store event in outbox (same transaction)
            var outbox = new SqlServerEventOutbox(
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

### Using IOutboxTransactionScope

```csharp
public class UnitOfWork : IOutboxTransactionScope, IAsyncDisposable
{
    private readonly SqlConnection _connection;
    private SqlTransaction? _transaction;

    public async Task BeginTransactionAsync(CancellationToken ct)
    {
        await _connection.OpenAsync(ct);
        _transaction = _connection.BeginTransaction();
    }

    public object? GetCurrentTransaction() => _transaction;

    public async Task CommitAsync(CancellationToken ct)
    {
        if (_transaction is not null)
        {
            await _transaction.CommitAsync(ct);
        }
    }
}

// Register in DI
services.AddScoped<IOutboxTransactionScope, UnitOfWork>();
```

## Schema Customization

To use a custom schema or table name, update the `:setvar` variables at the top of `OutboxMessage.sql` before executing, then configure the same names in code:

```sql
:setvar SchemaName "myapp"
:setvar TableName "Events"
```

```csharp
services.AddPulse(config => config
    .AddOutbox(options =>
    {
        options.Schema = "myapp";      // Default: "pulse"
        options.TableName = "Events";  // Default: "OutboxMessage"
    })
    .AddSqlServerOutbox(connectionString)
);
```

## Performance Considerations

### Indexing

The default schema includes optimized indexes for:

- Pending message polling (`Status`, `CreatedAt`)
- Completed message cleanup (`Status`, `ProcessedAt`)

### Stored Procedures

Operations use stored procedures with:

- `ROWLOCK` for row-level locking
- `READPAST` to skip locked rows during polling
- `SET NOCOUNT ON` to reduce network traffic

### Batch Processing

Configure batch size based on your throughput requirements:

```csharp
.AddOutbox(processorOptions: options =>
{
    options.BatchSize = 500;                    // Messages per poll
    options.PollingInterval = TimeSpan.FromSeconds(1);
    options.EnableBatchSending = true;          // Use batch transport
})
```

## Schema Interchangeability

This provider uses the canonical outbox schema, making it fully interchangeable with the Entity Framework provider:

1. **Development**: Start with Entity Framework for rapid iteration
2. **Production**: Switch to ADO.NET for maximum performance
3. **Mixed**: Use both providers against the same database

```csharp
// Both configurations work with the same database table
.AddSqlServerOutbox(connectionString)
// or
.AddEntityFrameworkOutbox<MyDbContext>()
```

## Requirements

- .NET 8.0, .NET 9.0, or .NET 10.0
- SQL Server 2016 or later (or Azure SQL Database)
- `Microsoft.Data.SqlClient` for database connectivity
- `Microsoft.Extensions.Hosting` for the background processor

## Related Packages

- [**NetEvolve.Pulse**](https://www.nuget.org/packages/NetEvolve.Pulse/) - Core mediator and outbox abstractions
- [**NetEvolve.Pulse.Dapr**](https://www.nuget.org/packages/NetEvolve.Pulse.Dapr/) - Dapr pub/sub integration for event dispatch
- [**NetEvolve.Pulse.Extensibility**](https://www.nuget.org/packages/NetEvolve.Pulse.Extensibility/) - Core contracts and abstractions
- [**NetEvolve.Pulse.EntityFramework**](https://www.nuget.org/packages/NetEvolve.Pulse.EntityFramework/) - Entity Framework persistence
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
