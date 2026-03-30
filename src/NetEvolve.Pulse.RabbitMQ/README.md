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

```csharp
services.AddPulse(config =>
{
    config.UseRabbitMqTransport(options =>
    {
        options.HostName = "rabbitmq.example.com";
        options.ExchangeName = "events";
        
        // Resolve routing key from event type
        options.RoutingKeyResolver = message =>
        {
            var eventType = message.EventType.Split(',')[0].Split('.').Last();
            return $"events.{eventType.ToLowerInvariant()}";
        };
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
| `RoutingKey` | `string` | `""` | Default routing key |
| `RoutingKeyResolver` | `Func<OutboxMessage, string>?` | `null` | Custom routing key resolution |

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
