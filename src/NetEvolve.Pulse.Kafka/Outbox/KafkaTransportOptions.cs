namespace NetEvolve.Pulse.Outbox;

using Confluent.Kafka;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Configuration options for the Kafka message transport.
/// </summary>
public sealed class KafkaTransportOptions
{
    /// <summary>
    /// Gets or sets the comma-separated list of broker addresses (e.g., <c>localhost:9092</c>).
    /// </summary>
    public string BootstrapServers { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the default Kafka topic to produce outbox messages to.
    /// </summary>
    public string DefaultTopic { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional delegate to resolve the target Kafka topic for a given outbox message.
    /// When provided, takes precedence over <see cref="DefaultTopic" />.
    /// </summary>
    public Func<OutboxMessage, string>? TopicResolver { get; set; }

    /// <summary>
    /// Gets or sets additional producer configuration passed through to the Confluent.Kafka producer.
    /// </summary>
    /// <remarks>
    /// The <c>BootstrapServers</c> property is always overridden by the value of
    /// <see cref="BootstrapServers" />. The <c>Acks</c> setting is always forced to <c>Acks.All</c>
    /// for durability guarantees.
    /// </remarks>
    public ProducerConfig? ProducerConfig { get; set; }
}
