# NetEvolve.Pulse.Dapr

[![NuGet Version](https://img.shields.io/nuget/v/NetEvolve.Pulse.Dapr.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.Dapr/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NetEvolve.Pulse.Dapr.svg)](https://www.nuget.org/packages/NetEvolve.Pulse.Dapr/)
[![License](https://img.shields.io/github/license/dailydevops/pulse.svg)](https://github.com/dailydevops/pulse/blob/main/LICENSE)

Dapr pub/sub transport for the Pulse outbox pattern. Publishes outbox messages to Dapr topics via `DaprClient`, enabling reliable event delivery to any message broker supported by Dapr—Redis, Kafka, Azure Service Bus, RabbitMQ, and more—without changing application code.

## Features

* **Dapr pub/sub**: Publish outbox messages to any Dapr-supported message broker
* **CloudEvents**: Payload is forwarded as CloudEvent data via `DaprClient.PublishEventAsync`
* **Health checks**: Delegates to `DaprClient.CheckHealthAsync` for readiness probing
* **Configurable topic resolution**: Map event types to topic names via a custom resolver function
* **Broker-agnostic**: Switch brokers by changing the Dapr component configuration — no code changes required

## Installation

### NuGet Package Manager

```powershell
Install-Package NetEvolve.Pulse.Dapr
```

### .NET CLI

```bash
dotnet add package NetEvolve.Pulse.Dapr
```

### PackageReference

```xml
<PackageReference Include="NetEvolve.Pulse.Dapr" Version="x.x.x" />
```

## Quick Start

### 1. Add the Dapr client package

```bash
dotnet add package Dapr.AspNetCore
```

### 2. Register services

```csharp
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse;

var services = new ServiceCollection();

// DaprClient must be registered before UseDaprTransport
services.AddDaprClient();

services.AddPulse(config => config
    .AddOutbox(
        options => options.Schema = "pulse",
        processorOptions => processorOptions.BatchSize = 100)
    .UseDaprTransport(options =>
    {
        options.PubSubName = "pubsub"; // Dapr pub/sub component name
    }));
```

### 3. Store events via IEventOutbox

Use `IEventOutbox` to store events reliably. The outbox processor picks them up and publishes each one to the configured Dapr topic:

```csharp
public class OrderService
{
    private readonly IEventOutbox _outbox;

    public OrderService(IEventOutbox outbox) => _outbox = outbox;

    public async Task CreateOrderAsync(CreateOrderRequest request, CancellationToken ct)
    {
        // ... business logic ...

        // Stored reliably; published via Dapr when the processor runs
        await _outbox.StoreAsync(new OrderCreatedEvent
        {
            OrderId = Guid.NewGuid(),
            CustomerId = request.CustomerId
        }, ct);
    }
}
```

## Transaction Integration

For reliable at-least-once delivery guarantees, store outbox events within the same database transaction as your business data. Pair the Dapr transport with a persistence provider that supports transaction enlistment:

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
> The Dapr transport only handles *publishing*. Transactional guarantees are provided by the persistence layer (e.g., `NetEvolve.Pulse.EntityFramework` or `NetEvolve.Pulse.SqlServer`).

## Subscriber Integration

Dapr subscribers receive the published events as CloudEvents. Use `Dapr.AspNetCore` to subscribe to topics in ASP.NET Core:

```csharp
// Program.cs — enable Dapr subscriber routing
app.MapSubscribeHandler();
```

```csharp
// OrdersController.cs
using Dapr;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("[controller]")]
public class OrdersController : ControllerBase
{
    [Topic("pubsub", "OrderCreated")]
    [HttpPost("order-created")]
    public async Task<IActionResult> OnOrderCreated(
        [FromBody] OrderCreatedEvent evt,
        CancellationToken ct)
    {
        // Handle the event
        return Ok();
    }
}
```

Or use a declarative subscription component:

```yaml
apiVersion: dapr.io/v1alpha1
kind: Subscription
metadata:
  name: order-created-subscription
spec:
  pubsubname: pubsub
  topic: OrderCreated
  route: /orders/order-created
```

## Configuration

### `DaprMessageTransportOptions`

| Property | Type | Default | Description |
|---|---|---|---|
| `PubSubName` | `string` | `"pubsub"` | Name of the Dapr pub/sub component |
| `TopicNameResolver` | `Func<OutboxMessage, string>` | Simple class name | Resolves the topic name from an outbox message |

### Topic Name Resolution

By default, the simple class name of the event type is used as the topic name. The assembly qualifier and namespace are stripped automatically.

| `EventType` | Resolved topic |
|---|---|
| `MyApp.Events.OrderCreated, MyApp` | `OrderCreated` |
| `MyApp.Events.PaymentProcessed, MyApp` | `PaymentProcessed` |

Override the resolver for custom naming strategies:

```csharp
.UseDaprTransport(options =>
{
    options.PubSubName = "servicebus-pubsub";
    options.TopicNameResolver = msg =>
    {
        // Use the full namespace-qualified type name as topic (without assembly)
        var typeName = msg.EventType;
        var commaIndex = typeName.IndexOf(',', StringComparison.Ordinal);
        return commaIndex > 0 ? typeName[..commaIndex] : typeName;
    };
});
```

## Dapr Component Examples

### Redis Streams (local development)

```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: pubsub
spec:
  type: pubsub.redis
  version: v1
  metadata:
    - name: redisHost
      value: "localhost:6379"
    - name: redisPassword
      value: ""
```

### Azure Service Bus

```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: pubsub
spec:
  type: pubsub.azure.servicebus.topics
  version: v1
  metadata:
    - name: connectionString
      value: "Endpoint=sb://..."
```

### RabbitMQ

```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: pubsub
spec:
  type: pubsub.rabbitmq
  version: v1
  metadata:
    - name: host
      value: "amqp://guest:guest@localhost:5672"
```

## Broker Switching

Because all broker configuration lives in the Dapr component YAML, switching brokers requires no code changes:

```csharp
// This registration never changes, regardless of the broker
services.AddPulse(config => config
    .AddOutbox()
    .UseDaprTransport(options => options.PubSubName = "pubsub"));
```

To switch from Redis to Azure Service Bus, update the component YAML and redeploy — application code stays identical.

## How It Works

1. Your application stores events in the outbox via `IEventOutbox.StoreAsync` within a database transaction.
2. The Pulse background processor polls the outbox for pending messages.
3. For each message, `DaprMessageTransport` deserializes the stored JSON payload and calls `DaprClient.PublishEventAsync` with the resolved pub/sub component name and topic.
4. Dapr delivers the CloudEvent to subscribers on the configured broker.
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

### Singleton Transport

`DaprMessageTransport` is registered as a singleton. `DaprClient` is thread-safe and designed for concurrent use, so no additional synchronization is required.

## Requirements

* .NET 8.0, .NET 9.0, or .NET 10.0
* Dapr runtime 1.13+
* `Dapr.AspNetCore` (or `Dapr.Client`) with a registered `DaprClient` in the DI container
* `Microsoft.Extensions.DependencyInjection` for service registration
* `Microsoft.Extensions.Hosting` for the background processor

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

* **Issues**: Report bugs or request features on [GitHub Issues](https://github.com/dailydevops/pulse/issues)
* **Documentation**: Read the full documentation at [https://github.com/dailydevops/pulse](https://github.com/dailydevops/pulse)

## License

This project is licensed under the MIT License - see the [LICENSE](https://github.com/dailydevops/pulse/blob/main/LICENSE) file for details.

---

> [!NOTE]
> **Made with ❤️ by the NetEvolve Team**
