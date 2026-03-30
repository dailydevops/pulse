# NetEvolve.Pulse.RabbitMQ

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.RabbitMQ.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.RabbitMQ/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.RabbitMQ.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.RabbitMQ/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

RabbitMQ transport for the Pulse outbox pattern. Publishes outbox messages directly to RabbitMQ exchanges using the official .NET client, enabling reliable event delivery without routing through Dapr or other intermediaries.

## Features

- **Direct Publishing**: Send messages to RabbitMQ exchanges without additional infrastructure
- **Flexible Routing**: Automatic routing key resolution based on event types via `ITopicNameResolver`
- **Health Checks**: Verify connection and channel state for readiness probing
- **Batch Support**: Efficient batch publishing using parallel execution (default implementation)
- **Connection Management**: Singleton connection with lazy channel initialization

## Installation

### NuGet Package Manager

```powershell
Install-Package NetEvolve.Pulse.RabbitMQ
```

### .NET CLI

```bash
dotnet add package NetEvolve.Pulse.RabbitMQ
```

### PackageReference

```xml
<PackageReference Include="NetEvolve.Pulse.RabbitMQ" Version="x.x.x" />
```

## Quick Start

### 1. Add the RabbitMQ client package

```bash
dotnet add package RabbitMQ.Client
```

### 2. Register services

```csharp
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse;
using RabbitMQ.Client;

var services = new ServiceCollection();

// Register RabbitMQ connection before UseRabbitMqTransport
services.AddSingleton<IConnection>(sp =>
{
    var factory = new ConnectionFactory
    {
        HostName = "localhost",
        Port = 5672,
        VirtualHost = "/",
        UserName = "guest",
        Password = "guest"
    };
    return factory.CreateConnectionAsync().GetAwaiter().GetResult();
});

services.AddPulse(config => config
    .AddOutbox(
        options => options.Schema = "pulse",
        processorOptions => processorOptions.BatchSize = 100)
    .UseRabbitMqTransport(options =>
    {
        options.ExchangeName = "events";
    }));
```

### 3. Store events via IEventOutbox

Use `IEventOutbox` to store events reliably. The outbox processor picks them up and publishes each one to the configured RabbitMQ exchange:

```csharp
public class OrderService
{
    private readonly IEventOutbox _outbox;

    public OrderService(IEventOutbox outbox) => _outbox = outbox;

    public async Task CreateOrderAsync(CreateOrderRequest request, CancellationToken ct)
    {
        // ... business logic ...

        // Stored reliably; published via RabbitMQ when the processor runs
        await _outbox.StoreAsync(new OrderCreatedEvent
        {
            OrderId = Guid.NewGuid(),
            CustomerId = request.CustomerId
        }, ct);
    }
}
```

## Transaction Integration

For reliable at-least-once delivery guarantees, store outbox events within the same database transaction as your business data. Pair the RabbitMQ transport with a persistence provider that supports transaction enlistment:

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

> [!NOTE]
> The RabbitMQ transport only handles _publishing_. Transactional guarantees are provided by the persistence layer (e.g., `NetEvolve.Pulse.EntityFramework` or `NetEvolve.Pulse.SqlServer`).

## Configuration

### `RabbitMqTransportOptions`

| Property       | Type     | Default | Description                                   |
| -------------- | -------- | ------- | --------------------------------------------- |
| `ExchangeName` | `string` | `""`    | Target exchange for publishing (**required**) |

### Routing Key Resolution

By default, the simple class name of the event type is used as the routing key. The assembly qualifier and namespace are stripped automatically via `ITopicNameResolver`.

| `EventType`                            | Resolved routing key |
| -------------------------------------- | -------------------- |
| `MyApp.Events.OrderCreated, MyApp`     | `OrderCreated`       |
| `MyApp.Events.PaymentProcessed, MyApp` | `PaymentProcessed`   |

Override the resolver for custom naming strategies:

```csharp
services.AddSingleton<ITopicNameResolver, MyCustomTopicNameResolver>();

services.AddPulse(config => config
    .UseRabbitMqTransport(options =>
    {
        options.ExchangeName = "events";
    }));
```

## Exchange Setup

> [!IMPORTANT]
> The target RabbitMQ exchange must already exist. This transport does not auto-declare exchanges or queues.

### Example: Topic Exchange

```bash
# Create a topic exchange for event routing
rabbitmqadmin declare exchange name=events type=topic durable=true

# Create queues and bind them to specific event types
rabbitmqadmin declare queue name=order-service durable=true
rabbitmqadmin declare binding source=events destination=order-service routing_key="OrderCreated"

rabbitmqadmin declare queue name=payment-service durable=true
rabbitmqadmin declare binding source=events destination=payment-service routing_key="PaymentProcessed"
```

### Example: Fanout Exchange

```bash
# Create a fanout exchange for broadcasting to all subscribers
rabbitmqadmin declare exchange name=notifications type=fanout durable=true

# Create queues and bind them (no routing key needed for fanout)
rabbitmqadmin declare queue name=email-service durable=true
rabbitmqadmin declare binding source=notifications destination=email-service

rabbitmqadmin declare queue name=sms-service durable=true
rabbitmqadmin declare binding source=notifications destination=sms-service
```

## Consumer Integration

Consume messages using the official RabbitMQ .NET client or any compatible library:

```csharp
var factory = new ConnectionFactory { HostName = "localhost" };
await using var connection = await factory.CreateConnectionAsync();
await using var channel = await connection.CreateChannelAsync();

await channel.QueueDeclareAsync(
    queue: "order-service",
    durable: true,
    exclusive: false,
    autoDelete: false);

await channel.QueueBindAsync(
    queue: "order-service",
    exchange: "events",
    routingKey: "OrderCreated");

var consumer = new AsyncEventingBasicConsumer(channel);
consumer.ReceivedAsync += async (sender, ea) =>
{
    var body = ea.Body.ToArray();
    var json = Encoding.UTF8.GetString(body);
    var @event = JsonSerializer.Deserialize<OrderCreatedEvent>(json);
    
    // Handle the event
    Console.WriteLine($"Order created: {@event.OrderId}");
    
    await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
};

await channel.BasicConsumeAsync(
    queue: "order-service",
    autoAck: false,
    consumer: consumer);
```

## How It Works

1. Your application stores events in the outbox via `IEventOutbox.StoreAsync` within a database transaction.
2. The Pulse background processor polls the outbox for pending messages.
3. For each message, `RabbitMqMessageTransport` publishes it to the configured exchange with a routing key resolved by `ITopicNameResolver`.
4. RabbitMQ routes the message to bound queues based on the routing key and exchange type.
5. On success, the message is marked as processed; on failure, it remains pending for the next poll cycle.

## Performance Considerations

### Batch Processing

Configure batch size and polling interval based on your throughput requirements:

```csharp
.AddOutbox(processorOptions: options =>
{
    options.BatchSize = 100;                         // Messages per poll cycle
    options.PollingInterval = TimeSpan.FromSeconds(1);
})
```

### Connection Management

Register `IConnection` as a singleton in your DI container. The RabbitMQ client library is thread-safe and designed for concurrent use, so a single shared connection is recommended.

### Channel Management

Channels are created on demand and reused for subsequent sends. If a channel becomes closed, a new one is automatically created on the next send operation.

## Requirements

- .NET 8.0, .NET 9.0, or .NET 10.0
- RabbitMQ 3.8+ (or compatible AMQP 0-9-1 broker)
- `RabbitMQ.Client` 7.0+ for async API support
- `Microsoft.Extensions.DependencyInjection` for service registration
- `Microsoft.Extensions.Hosting` for the background processor

## Related Packages

- [**NetEvolve.Pulse**](https://www.nuget.org/packages/NetEvolve.Pulse/) - Core mediator and outbox abstractions
- [**NetEvolve.Pulse.Extensibility**](https://www.nuget.org/packages/NetEvolve.Pulse.Extensibility/) - Core contracts and abstractions
- [**NetEvolve.Pulse.EntityFramework**](https://www.nuget.org/packages/NetEvolve.Pulse.EntityFramework/) - Entity Framework Core persistence provider
- [**NetEvolve.Pulse.SqlServer**](https://www.nuget.org/packages/NetEvolve.Pulse.SqlServer/) - SQL Server ADO.NET persistence provider
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
