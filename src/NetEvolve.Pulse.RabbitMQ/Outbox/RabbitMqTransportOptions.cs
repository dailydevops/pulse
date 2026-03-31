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
}
