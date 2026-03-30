# NetEvolve.Pulse.Kafka

Apache Kafka transport for the [NetEvolve.Pulse](https://github.com/dailydevops/pulse) outbox processor.

Delivers outbox messages directly to Kafka topics using the [Confluent.Kafka](https://github.com/confluentinc/confluent-kafka-dotnet) producer with `Acks.All` for durability guarantees.

## Getting Started

```csharp
services.AddPulse(config =>
    config.UseKafkaTransport(options =>
    {
        options.BootstrapServers = "localhost:9092";
        options.DefaultTopic = "outbox-events";
    })
);
```

## Configuration

| Property | Description |
|---|---|
| `BootstrapServers` | Comma-separated list of broker addresses (required). |
| `DefaultTopic` | Default Kafka topic for outbox messages. |
| `TopicResolver` | Optional delegate to resolve the topic per message (overrides `DefaultTopic`). |
| `ProducerConfig` | Optional passthrough for advanced Confluent.Kafka producer settings. |

## Topic Resolution

Supply a `TopicResolver` delegate to route messages to different topics based on event type:

```csharp
options.TopicResolver = message => message.EventType switch
{
    "OrderCreated" => "orders",
    "PaymentProcessed" => "payments",
    _ => options.DefaultTopic,
};
```

## Notes

- `BootstrapServers` always overrides the same property in `ProducerConfig`.
- `Acks` is always forced to `Acks.All` for durability.
- `IsHealthyAsync` queries cluster metadata; returns `false` when the broker is unreachable.
