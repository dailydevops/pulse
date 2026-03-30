namespace NetEvolve.Pulse.Outbox;

using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Configuration options for <see cref="RabbitMqMessageTransport"/>.
/// </summary>
public sealed class RabbitMqTransportOptions
{
    /// <summary>
    /// Gets or sets the RabbitMQ host name.
    /// </summary>
    /// <remarks>Defaults to <c>"localhost"</c>.</remarks>
    public string HostName { get; set; } = "localhost";

    /// <summary>
    /// Gets or sets the RabbitMQ port.
    /// </summary>
    /// <remarks>Defaults to <c>5672</c>.</remarks>
    public int Port { get; set; } = 5672;

    /// <summary>
    /// Gets or sets the RabbitMQ virtual host.
    /// </summary>
    /// <remarks>Defaults to <c>"/"</c>.</remarks>
    public string VirtualHost { get; set; } = "/";

    /// <summary>
    /// Gets or sets the user name for RabbitMQ authentication.
    /// </summary>
    /// <remarks>Defaults to <c>"guest"</c>.</remarks>
    public string UserName { get; set; } = "guest";

    /// <summary>
    /// Gets or sets the password for RabbitMQ authentication.
    /// </summary>
    /// <remarks>Defaults to <c>"guest"</c>.</remarks>
    public string Password { get; set; } = "guest";

    /// <summary>
    /// Gets or sets the target exchange name for publishing messages.
    /// </summary>
    /// <remarks>
    /// This is the RabbitMQ exchange where all outbox messages will be published.
    /// The exchange must already exist; it will not be auto-declared.
    /// </remarks>
    public string ExchangeName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the routing key prefix for messages.
    /// </summary>
    /// <remarks>
    /// If set, this prefix is prepended to the resolved topic name (event type) to form the final routing key.
    /// For example, if <c>RoutingKey = "events"</c> and the topic name is <c>"OrderCreated"</c>,
    /// the final routing key will be <c>"events.OrderCreated"</c>.
    /// Can be empty for fanout exchanges or when no prefix is desired.
    /// </remarks>
    public string RoutingKey { get; set; } = string.Empty;
}
