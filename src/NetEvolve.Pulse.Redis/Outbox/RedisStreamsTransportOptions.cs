namespace NetEvolve.Pulse.Outbox;

/// <summary>
/// Configuration options for <see cref="RedisStreamsMessageTransport"/>.
/// </summary>
public sealed class RedisStreamsTransportOptions
{
    /// <summary>
    /// Gets or sets the key of the Redis stream used for publishing outbox messages.
    /// </summary>
    public string StreamKey { get; set; } = "pulse:outbox";

    /// <summary>
    /// Gets or sets the name of the consumer group used to read messages from the stream.
    /// </summary>
    public string ConsumerGroupName { get; set; } = "pulse-processor";

    /// <summary>
    /// Gets or sets the name of the consumer within the consumer group.
    /// </summary>
    public string ConsumerName { get; set; } = Environment.MachineName;

    /// <summary>
    /// Gets or sets the Redis database index to use.
    /// </summary>
    /// <remarks>
    /// -1 selects the connection multiplexer's default database.
    /// </remarks>
    public int Database { get; set; } = -1;

    /// <summary>
    /// Gets or sets a value indicating whether the stream and consumer group should be created if they do not already exist.
    /// </summary>
    public bool CreateStreamIfNotExists { get; set; } = true;
}
