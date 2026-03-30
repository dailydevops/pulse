namespace NetEvolve.Pulse.Outbox;

using System.Text;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Outbox;
using RabbitMQ.Client;

/// <summary>
/// Message transport that publishes outbox messages to RabbitMQ exchanges.
/// </summary>
/// <remarks>
/// <para><strong>Connection Management:</strong></para>
/// This transport uses an injected <see cref="IConnection"/> and creates channels on demand.
/// The connection lifetime is managed externally via dependency injection.
/// <para><strong>Routing Key Resolution:</strong></para>
/// Each message is published with a routing key resolved by <see cref="ITopicNameResolver"/>.
/// By default, the simple class name of the event type is used (e.g., <c>"OrderCreated"</c>).
/// <para><strong>Payload:</strong></para>
/// The raw JSON payload from <see cref="OutboxMessage.Payload"/> is published as the message body.
/// <para><strong>Health Checks:</strong></para>
/// The <see cref="IsHealthyAsync"/> method verifies that the connection and channel are open.
/// </remarks>
internal sealed class RabbitMqMessageTransport : IMessageTransport, IDisposable
{
    /// <summary>The resolved transport options controlling the RabbitMQ connection and exchange settings.</summary>
    private readonly RabbitMqTransportOptions _options;

    /// <summary>The topic name resolver used to determine the routing key from an outbox message.</summary>
    private readonly ITopicNameResolver _topicNameResolver;

    /// <summary>The RabbitMQ connection provided via dependency injection.</summary>
    private readonly IConnection _connection;

    /// <summary>Lazy-initialized RabbitMQ channel for publishing.</summary>
    private IChannel? _channel;

    /// <summary>Semaphore for thread-safe channel initialization.</summary>
    private readonly SemaphoreSlim _initializationLock = new(1, 1);

    /// <summary>Indicates whether the transport has been disposed.</summary>
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMqMessageTransport"/> class.
    /// </summary>
    /// <param name="connection">The RabbitMQ connection.</param>
    /// <param name="topicNameResolver">The topic name resolver for determining routing keys from outbox messages.</param>
    /// <param name="options">The transport options.</param>
    public RabbitMqMessageTransport(
        IConnection connection,
        ITopicNameResolver topicNameResolver,
        IOptions<RabbitMqTransportOptions> options
    )
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(topicNameResolver);
        ArgumentNullException.ThrowIfNull(options);

        _connection = connection;
        _topicNameResolver = topicNameResolver;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var channel = await EnsureChannelAsync(cancellationToken).ConfigureAwait(false);
        var routingKey = ResolveRoutingKey(message);
        var body = Encoding.UTF8.GetBytes(message.Payload);

        var properties = new BasicProperties
        {
            MessageId = message.Id.ToString(),
            CorrelationId = message.CorrelationId,
            ContentType = "application/json",
            Timestamp = new AmqpTimestamp(message.CreatedAt.ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?>
            {
                ["eventType"] = message.EventType,
                ["retryCount"] = message.RetryCount,
            },
        };

        await channel
            .BasicPublishAsync(
                exchange: _options.ExchangeName,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_connection?.IsOpen != true || _channel?.IsOpen != true)
            {
                return false;
            }

            // Perform a lightweight check by verifying channel is still open
            // RabbitMQ client maintains the connection state internally
            return await Task.FromResult(_channel.IsOpen).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ensures that a channel is available, creating it if necessary.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The initialized channel.</returns>
    private async Task<IChannel> EnsureChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel?.IsOpen == true)
        {
            return _channel;
        }

        await _initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_channel?.IsOpen == true)
            {
                return _channel;
            }

            _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

            return _channel;
        }
        finally
        {
            _ = _initializationLock.Release();
        }
    }

    /// <summary>
    /// Resolves the routing key for a given outbox message.
    /// </summary>
    /// <param name="message">The outbox message to resolve the routing key from.</param>
    /// <returns>The resolved routing key.</returns>
    private string ResolveRoutingKey(OutboxMessage message) => _topicNameResolver.Resolve(message);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _channel?.Dispose();
        _initializationLock.Dispose();
        _disposed = true;
    }
}
