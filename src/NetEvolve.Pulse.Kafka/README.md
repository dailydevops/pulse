# NetEvolve.Pulse.Kafka

Apache Kafka transport for the [NetEvolve.Pulse](https://github.com/dailydevops/pulse) outbox processor.

Delivers outbox messages directly to Kafka topics using the [Confluent.Kafka](https://github.com/confluentinc/confluent-kafka-dotnet) producer.

## Getting Started

Register the Confluent.Kafka producer and admin client in DI, then call `UseKafkaTransport()`:

```csharp
// 1. Register the Confluent.Kafka producer (user's responsibility)
services.AddSingleton<IProducer<string, string>>(sp =>
    new ProducerBuilder<string, string>(
        new ProducerConfig { BootstrapServers = "localhost:9092", Acks = Acks.All })
    .Build());

// 2. Register the admin client (used for health checks)
services.AddSingleton<IAdminClient>(sp =>
    new AdminClientBuilder(
        new AdminClientConfig { BootstrapServers = "localhost:9092" })
    .Build());

// 3. Register the Pulse Kafka transport
services.AddPulse(config => config.AddOutbox().UseKafkaTransport());
```

## Topic Routing

Topic names are resolved by the registered `ITopicNameResolver`. The default implementation
(registered by `AddOutbox()`) extracts the simple class name from `OutboxMessage.EventType`,
e.g. `"MyApp.Events.OrderCreated, MyApp"` → `"OrderCreated"`.

Register a custom `ITopicNameResolver` **before** calling `UseKafkaTransport()` to override:

```csharp
services.AddSingleton<ITopicNameResolver, MyCustomTopicNameResolver>();
services.AddPulse(config => config.AddOutbox().UseKafkaTransport());
```

## Notes

- `IProducer<string, string>` and `IAdminClient` must be registered by the caller.
- `IsHealthyAsync` queries cluster metadata; returns `false` when the broker is unreachable.
