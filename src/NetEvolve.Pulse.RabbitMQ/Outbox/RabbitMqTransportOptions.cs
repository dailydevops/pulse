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
    /// Gets or sets the default routing key for messages.
    /// </summary>
    /// <remarks>
    /// This routing key is used by default unless overridden by a custom routing key resolver.
    /// Can be empty for fanout exchanges.
    /// </remarks>
    public string RoutingKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional delegate to resolve the routing key from an outbox message.
    /// </summary>
    /// <remarks>
    /// If provided, this delegate is called to determine the routing key for each message.
    /// If null, <see cref="RoutingKey"/> is used for all messages.
    /// </remarks>
    public Func<OutboxMessage, string>? RoutingKeyResolver { get; set; }
}
