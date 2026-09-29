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

// 2. Register the admin client (used for topic auto-creation and health checks)
services.AddSingleton<IAdminClient>(sp =>
    new AdminClientBuilder(
        new AdminClientConfig { BootstrapServers = "localhost:9092" })
    .Build());

// 3. Register the Pulse Kafka transport
services.AddPulse(config => config.AddOutbox().UseKafkaTransport());
```

## Topic Routing

Topic names are resolved by the registered `ITopicNameResolver`. The default implementation
(registered by `AddOutbox()`) returns the `Type.Name` of `OutboxMessage.EventType`, which is a `System.Type`,
e.g. an event of type `MyApp.Events.OrderCreated` resolves to `"OrderCreated"`.

Register a custom `ITopicNameResolver` **before** calling `UseKafkaTransport()` to override:

```csharp
services.AddSingleton<ITopicNameResolver, MyCustomTopicNameResolver>();
services.AddPulse(config => config.AddOutbox().UseKafkaTransport());
```

## Topic Auto-Creation

`AutoCreateTopics` is `true` by default. Before the first message to a topic, the transport calls
`IAdminClient.CreateTopicsAsync` with these `KafkaTransportOptions`. It caches the topic once creation succeeds,
reports `TOPIC_ALREADY_EXISTS` or is rejected with an authorization error. Any other failure is retried on the next
send (once per topic within a batch):

| Option | Default | Description |
|---|---|---|
| `AutoCreateTopics` | `true` | Create each topic before its first message. |
| `DefaultPartitionCount` | `1` | Partition count of auto-created topics. |
| `DefaultReplicationFactor` | `1` | Replication factor of auto-created topics. Most production clusters need `3`. |
| `MessageRetention` | `null` | `retention.ms` of auto-created topics; `null` uses the broker default. |

Creating a topic needs the `CREATE` ACL on the cluster or on the topic. When the broker answers with
`TOPIC_AUTHORIZATION_FAILED` or `CLUSTER_AUTHORIZATION_FAILED`, the transport stops trying to create that topic
and produces to it anyway. If the topic cannot be described either, it logs a warning. Any other creation error
(for example `POLICY_VIOLATION` or `INVALID_REPLICATION_FACTOR`) fails the send with an
`InvalidOperationException` that names the topic.

On managed clusters with pre-provisioned topics, turn auto-creation off:

```csharp
services.AddPulse(config => config.AddOutbox().UseKafkaTransport(options => options.AutoCreateTopics = false));
```

## Notes

- `IProducer<string, string>` and `IAdminClient` must be registered by the caller.
- `IsHealthyAsync` queries cluster metadata; returns `false` when the broker is unreachable.
