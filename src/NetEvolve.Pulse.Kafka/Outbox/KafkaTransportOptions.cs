namespace NetEvolve.Pulse.Outbox;

/// <summary>
/// Configuration options for <see cref="KafkaMessageTransport"/>.
/// </summary>
public sealed class KafkaTransportOptions
{
    /// <summary>
    /// Gets or sets the default number of partitions for auto-created topics.
    /// </summary>
    /// <remarks>Defaults to <c>1</c>.</remarks>
    public int DefaultPartitionCount { get; set; } = 1;

    /// <summary>
    /// Gets or sets the default replication factor for auto-created topics.
    /// </summary>
    /// <remarks>Defaults to <c>1</c>.</remarks>
    public short DefaultReplicationFactor { get; set; } = 1;

    /// <summary>
    /// Gets or sets a value indicating whether topics should be automatically created before sending messages.
    /// </summary>
    /// <remarks>Defaults to <see langword="true"/>.</remarks>
    public bool AutoCreateTopics { get; set; } = true;

    /// <summary>
    /// Gets or sets the message retention duration applied to auto-created topics.
    /// </summary>
    /// <remarks>When <see langword="null"/>, the broker default retention policy is used.</remarks>
    public TimeSpan? MessageRetention { get; set; }
}
