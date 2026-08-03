namespace NetEvolve.Pulse.Outbox;

/// <summary>
/// Configuration options for <see cref="RabbitMqMessageTransport"/>.
/// </summary>
public sealed class RabbitMqTransportOptions
{
    /// <summary>
    /// Gets or sets the target exchange name for publishing messages.
    /// </summary>
    /// <remarks>
    /// This is the RabbitMQ exchange where all outbox messages will be published.
    /// The exchange must already exist; it will not be auto-declared.
    /// </remarks>
    public string ExchangeName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the maximum number of RabbitMQ channels that may be concurrently
    /// rented from the transport's channel pool.
    /// </summary>
    /// <remarks>
    /// Concurrent <see cref="RabbitMqMessageTransport.SendAsync"/> and
    /// <see cref="RabbitMqMessageTransport.SendBatchAsync"/> calls each rent their own
    /// channel from the pool instead of sharing (and serializing on) a single channel.
    /// This value caps how many channels may be rented at the same time; additional
    /// callers wait asynchronously for a channel to become available.
    /// </remarks>
    public int MaxChannelPoolSize { get; set; } = 10;
}
