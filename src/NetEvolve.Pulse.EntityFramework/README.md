# NetEvolve.Pulse.EntityFramework

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.EntityFramework.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.EntityFramework/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.EntityFramework.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.EntityFramework/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

Provider-agnostic Entity Framework Core persistence for the Pulse outbox pattern. Works with any EF Core database provider (SQL Server, PostgreSQL, SQLite, MySQL, etc.)—you bring your own provider and generate migrations. Features seamless transaction integration and uses the canonical outbox schema for interchangeability with ADO.NET providers.

## Features

* **Provider Agnostic**: Works with SQL Server, PostgreSQL, SQLite, MySQL, and any EF Core provider
* **Zero Provider Dependencies**: Only depends on `Microsoft.EntityFrameworkCore` abstractions
* **Transaction Integration**: Automatic participation in `DbContext` transactions
* **User-Generated Migrations**: Full control over database migrations with your chosen provider
* **Schema Interchangeability**: Uses canonical schema compatible with ADO.NET providers
* **LINQ Support**: Full Entity Framework query capabilities for advanced scenarios

## Installation

### NuGet Package Manager

```powershell
Install-Package NetEvolve.Pulse.EntityFramework
```

### .NET CLI

```bash
dotnet add package NetEvolve.Pulse.EntityFramework
```

### PackageReference

```xml
<PackageReference Include="NetEvolve.Pulse.EntityFramework" Version="x.x.x" />
```

## Quick Start

### 1. Implement IOutboxDbContext

```csharp
using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse;

public class ApplicationDbContext : DbContext, IOutboxDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // Your application entities
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Customer> Customers => Set<Customer>();

    // Required by IOutboxDbContext
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // The factory automatically selects the right configuration for the active provider
        modelBuilder.ApplyConfiguration(OutboxMessageConfigurationFactory.Create(this));

        // Or with custom options
        // modelBuilder.ApplyConfiguration(OutboxMessageConfigurationFactory.Create(this,
        //     Options.Create(new OutboxOptions { Schema = "myschema" })));
    }
}
```

### 2. Generate Migrations

```bash
# With your chosen provider (e.g., SQL Server)
dotnet add package Microsoft.EntityFrameworkCore.SqlServer

# Generate migration
dotnet ef migrations add AddOutbox --context ApplicationDbContext

# Apply migration
dotnet ef database update --context ApplicationDbContext
```

### 3. Register Services

```csharp
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse;

var services = new ServiceCollection();

// Register your DbContext with chosen provider
services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// Register Pulse with Entity Framework outbox
services.AddPulse(config => config
    .AddOutbox(
        options => options.Schema = "pulse",
        processorOptions => processorOptions.BatchSize = 100)
    .AddEntityFrameworkOutbox<ApplicationDbContext>()
);
```

## Transaction Integration

Events stored via `IEventOutbox` automatically participate in the current `DbContext` transaction:

```csharp
public class OrderService
{
    private readonly ApplicationDbContext _context;
    private readonly IEventOutbox _outbox;

    public OrderService(ApplicationDbContext context, IEventOutbox outbox)
    {
        _context = context;
        _outbox = outbox;
    }

    public async Task CreateOrderAsync(CreateOrderRequest request, CancellationToken ct)
    {
        // Begin transaction
        await using var transaction = await _context.Database.BeginTransactionAsync(ct);

        try
        {
            // Business operation
            var order = new Order { CustomerId = request.CustomerId, Total = request.Total };
            _context.Orders.Add(order);
            await _context.SaveChangesAsync(ct);

            // Store event in outbox (same transaction)
            await _outbox.StoreAsync(new OrderCreatedEvent
            {
                Id = Guid.NewGuid().ToString(),
                OrderId = order.Id,
                CustomerId = order.CustomerId
            }, ct);

            // Commit both business data and event atomically
            await transaction.CommitAsync(ct);
        }
        catch
        {
            // Rollback discards both business data AND the outbox event
            await transaction.RollbackAsync(ct);
            throw;
        }
    }
}
```

## Multi-Provider Support

`OutboxMessageConfigurationFactory.Create(this)` automatically picks the right column types
and index filter syntax for the active provider. Use it in `OnModelCreating` once and swap
providers without touching the DbContext:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.ApplyConfiguration(OutboxMessageConfigurationFactory.Create(this));
}
```

### SQL Server

```csharp
services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
```

### PostgreSQL

```csharp
services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));
```

### SQLite

```csharp
services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));
```

### MySQL

Supports both the Pomelo community provider and the Oracle official provider:

```csharp
// Pomelo (recommended community provider)
services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// Oracle MySQL provider
services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySQL(connectionString));
```

> [!NOTE]
> MySQL does not support filtered (partial) indexes. The pending and completed message
> indexes are created as plain indexes without a `WHERE` clause. The factory handles this
> automatically — no configuration changes are required.

### Other Providers

For providers not listed above, derive from `OutboxMessageConfigurationBase` directly
and return the appropriate filter syntax from `PendingMessagesFilter` and
`CompletedMessagesFilter` (return `null` for databases without partial-index support):

```csharp
internal sealed class MyCustomOutboxMessageConfiguration : OutboxMessageConfigurationBase
{
    public MyCustomOutboxMessageConfiguration(IOptions<OutboxOptions> options)
        : base(options) { }

    // Return null if the database does not support filtered indexes
    protected override string? PendingMessagesFilter => null;
    protected override string? CompletedMessagesFilter => null;
}

// In OnModelCreating:
modelBuilder.ApplyConfiguration(new MyCustomOutboxMessageConfiguration(
    Options.Create(new OutboxOptions())));
```

## Migration Examples

### SQL Server Migration Output

```csharp
// Generated migration snippet
migrationBuilder.EnsureSchema(name: "pulse");

migrationBuilder.CreateTable(
    name: "OutboxMessage",
    schema: "pulse",
    columns: table => new
    {
        Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
        EventType = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
        Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
        CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
        CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
        UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
        ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
        RetryCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
        Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
        Status = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
    },
    constraints: table =>
    {
        table.PrimaryKey("PK_OutboxMessage", x => x.Id);
    });

migrationBuilder.CreateIndex(
    name: "IX_OutboxMessage_Status_CreatedAt",
    schema: "pulse",
    table: "OutboxMessage",
    columns: new[] { "Status", "CreatedAt" });
```

## Schema Interchangeability

This provider produces the same table structure as the SQL Server ADO.NET provider, allowing you to:

1. **Switch providers**: Start with EF for development, move to ADO.NET for production
2. **Mix providers**: Use EF for writes (business transactions) and ADO.NET for reads (background processor)
3. **Migrate**: Change persistence strategy without data migration

```csharp
// Both work with the same database table
services.AddPulse(config => config
    .AddOutbox()
    // Choose one:
    .AddEntityFrameworkOutbox<MyDbContext>()
    // or:
    // .AddSqlServerOutbox(connectionString)
);
```

## Performance Considerations

For high-throughput scenarios, consider:

1. **Batch processing**: Configure `OutboxProcessorOptions.BatchSize`
2. **ADO.NET for polling**: Use SQL Server provider for background processor (explicit locking)
3. **Separate DbContext**: Dedicated context for outbox to avoid change tracker overhead

```csharp
// High-performance configuration
services.AddPulse(config => config
    .AddOutbox(processorOptions: options =>
    {
        options.BatchSize = 500;
        options.PollingInterval = TimeSpan.FromMilliseconds(500);
        options.EnableBatchSending = true;
    })
    .AddEntityFrameworkOutbox<ApplicationDbContext>()
);
```

## Requirements

* .NET 8.0, .NET 9.0, or .NET 10.0
* Entity Framework Core 8.0+ with your chosen database provider
* `Microsoft.Extensions.DependencyInjection` for service registration
* `Microsoft.Extensions.Hosting` for the background processor

## Related Packages

- [**NetEvolve.Pulse**](https://www.nuget.org/packages/NetEvolve.Pulse/) - Core mediator and outbox abstractions
- [**NetEvolve.Pulse.Dapr**](https://www.nuget.org/packages/NetEvolve.Pulse.Dapr/) - Dapr pub/sub integration for event dispatch
- [**NetEvolve.Pulse.Extensibility**](https://www.nuget.org/packages/NetEvolve.Pulse.Extensibility/) - Core contracts and abstractions
- [**NetEvolve.Pulse.SqlServer**](https://www.nuget.org/packages/NetEvolve.Pulse.SqlServer/) - SQL Server ADO.NET persistence
- [**NetEvolve.Pulse.Polly**](https://www.nuget.org/packages/NetEvolve.Pulse.Polly/) - Polly v8 resilience policies integration

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
