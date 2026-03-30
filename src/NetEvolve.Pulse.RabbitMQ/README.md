# NetEvolve.Pulse.RabbitMQ

RabbitMQ transport for the Pulse CQRS mediator outbox. Delivers outbox messages directly to RabbitMQ exchanges using the official .NET client, supporting single and batched sends with health checks for the broker connection.

## Features

- **Direct Publishing**: Send messages to RabbitMQ exchanges without routing through Dapr
- **Flexible Routing**: Configure default routing keys or use custom resolution per event type
- **Health Checks**: Verify connection and channel state before processing
- **Batch Support**: Efficient batch publishing using parallel execution
- **Connection Management**: Lazy connection initialization with automatic recovery

## Installation

```bash
dotnet add package NetEvolve.Pulse.RabbitMQ
```

## Usage

### Basic Configuration

```csharp
services.AddPulse(config =>
{
    config.UseRabbitMqTransport(options =>
    {
        options.HostName = "localhost";
        options.Port = 5672;
        options.UserName = "guest";
        options.Password = "guest";
        options.ExchangeName = "events";
        options.RoutingKey = "outbox.events";
    });
});
```

### Custom Routing Key Resolution

The transport uses `ITopicNameResolver` to extract the event type name (e.g., `OrderCreated` from `MyApp.Events.OrderCreated`). You can optionally prefix this with a routing key:

```csharp
services.AddPulse(config =>
{
    config.UseRabbitMqTransport(options =>
    {
        options.HostName = "rabbitmq.example.com";
        options.ExchangeName = "events";
        
        // Prefix all routing keys with "outbox"
        // Result: "outbox.OrderCreated", "outbox.UserRegistered", etc.
        options.RoutingKey = "outbox";
    });
});
```

To customize topic name resolution, register a custom `ITopicNameResolver` implementation:

```csharp
services.AddSingleton<ITopicNameResolver, MyCustomTopicNameResolver>();

services.AddPulse(config =>
{
    config.UseRabbitMqTransport(options =>
    {
        options.ExchangeName = "events";
        options.RoutingKey = "prefix"; // Optional prefix
    });
});
```

## Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `HostName` | `string` | `"localhost"` | RabbitMQ server hostname |
| `Port` | `int` | `5672` | RabbitMQ server port |
| `VirtualHost` | `string` | `"/"` | RabbitMQ virtual host |
| `UserName` | `string` | `"guest"` | Authentication username |
| `Password` | `string` | `"guest"` | Authentication password |
| `ExchangeName` | `string` | `""` | Target exchange for publishing (required) |
| `RoutingKey` | `string` | `""` | Routing key prefix (prepended to event type) |

## Prerequisites

- The target RabbitMQ exchange must already exist
- This transport does not auto-declare exchanges or queues
- RabbitMQ.Client 7.0+ is required for async API support

## Out of Scope

- Message consumption/subscription
- Exchange or queue auto-declaration
- Connection pooling (use a single transport instance)
- Advanced reconnection policies (consider combining with Polly)
- AMQP 1.0 protocol support
