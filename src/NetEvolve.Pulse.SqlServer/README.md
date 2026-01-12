# NetEvolve.Pulse.SqlServer

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.SqlServer.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.SqlServer/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.SqlServer.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.SqlServer/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

SQL Server persistence provider for the Pulse outbox pattern using plain ADO.NET. Provides optimized T-SQL operations with proper transaction support and locking strategies for reliable event delivery in high-throughput scenarios.

## Features

* **Plain ADO.NET**: No ORM overhead, direct SQL Server access via `Microsoft.Data.SqlClient`
* **Transaction Support**: Enlist outbox operations in existing `SqlTransaction` instances
* **Optimized Queries**: Uses stored procedures with ROWLOCK/READPAST hints for concurrent access
* **Configurable Schema**: Customize schema and table names for multi-tenant scenarios
* **Schema Interchangeability**: Uses canonical schema compatible with Entity Framework provider

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

Before using this provider, execute the schema script to create the required database objects:

```sql
-- Execute the embedded script from the package
-- Located at: content/Scripts/OutboxMessage.sql
```

Or run via your deployment pipeline:

```powershell
# Example using sqlcmd
sqlcmd -S your-server -d your-database -i OutboxMessage.sql
```

### Schema Script Contents

The script creates:
- `[pulse]` schema (configurable)
- `[pulse].[OutboxMessage]` table with optimized indexes
- Stored procedures for CRUD operations with proper locking

## Quick Start

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

```csharp
services.AddPulse(config => config
    .AddOutbox(options =>
    {
        options.Schema = "myapp";      // Default: "pulse"
        options.TableName = "Events";   // Default: "OutboxMessage"
    })
    .AddSqlServerOutbox(connectionString)
);
```

Remember to modify the SQL script accordingly when using custom schema/table names.

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

* .NET 8.0, .NET 9.0, or .NET 10.0
* SQL Server 2016 or later (or Azure SQL Database)
* `Microsoft.Data.SqlClient` for database connectivity
* `Microsoft.Extensions.Hosting` for the background processor

## Related Packages

- [NetEvolve.Pulse](https://www.nuget.org/packages/NetEvolve.Pulse/) - Core mediator and outbox abstractions
- [NetEvolve.Pulse.Extensibility](https://www.nuget.org/packages/NetEvolve.Pulse.Extensibility/) - Core contracts and abstractions
- [NetEvolve.Pulse.EntityFramework](https://www.nuget.org/packages/NetEvolve.Pulse.EntityFramework/) - Entity Framework persistence
- [NetEvolve.Pulse.Polly](https://www.nuget.org/packages/NetEvolve.Pulse.Polly/) - Polly v8 resilience policies integration

## Documentation

For complete documentation, please visit the [official documentation](https://github.com/dailydevops/pulse/blob/main/README.md).

## Contributing

Contributions are welcome! Please read the [Contributing Guidelines](https://github.com/dailydevops/pulse/blob/main/CONTRIBUTING.md) before submitting a pull request.

## Support

* **Issues**: Report bugs or request features on [GitHub Issues](https://github.com/dailydevops/pulse/issues)
* **Documentation**: Read the full documentation at [https://github.com/dailydevops/pulse](https://github.com/dailydevops/pulse)

## License

This project is licensed under the MIT License - see the [LICENSE](https://github.com/dailydevops/pulse/blob/main/LICENSE) file for details.

---

> [!NOTE] 
> **Made with ❤️ by the NetEvolve Team**
