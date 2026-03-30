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
/// This transport maintains a single connection and channel for publishing messages.
/// The connection is opened lazily on first use and kept alive for the lifetime of the transport.
/// <para><strong>Routing Key Resolution:</strong></para>
/// Each message is published with a routing key determined by <see cref="RabbitMqTransportOptions.RoutingKeyResolver"/>
/// if configured, otherwise <see cref="RabbitMqTransportOptions.RoutingKey"/> is used.
/// <para><strong>Payload:</strong></para>
/// The raw JSON payload from <see cref="OutboxMessage.Payload"/> is published as the message body.
/// <para><strong>Health Checks:</strong></para>
/// The <see cref="IsHealthyAsync"/> method verifies that the connection and channel are open.
/// </remarks>
internal sealed class RabbitMqMessageTransport : IMessageTransport, IDisposable
{
    /// <summary>The resolved transport options controlling the RabbitMQ connection and exchange settings.</summary>
    private readonly RabbitMqTransportOptions _options;

    /// <summary>Lazy-initialized RabbitMQ connection.</summary>
    private IConnection? _connection;

    /// <summary>Lazy-initialized RabbitMQ channel for publishing.</summary>
    private IChannel? _channel;

    /// <summary>Semaphore for thread-safe connection/channel initialization.</summary>
    private readonly SemaphoreSlim _initializationLock = new(1, 1);

    /// <summary>Indicates whether the transport has been disposed.</summary>
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMqMessageTransport"/> class.
    /// </summary>
    /// <param name="options">The transport options.</param>
    public RabbitMqMessageTransport(IOptions<RabbitMqTransportOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

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
            if (_connection is null || !_connection.IsOpen)
            {
                return false;
            }

            if (_channel is null || !_channel.IsOpen)
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
    /// Ensures that a connection and channel are available, creating them if necessary.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The initialized channel.</returns>
    private async Task<IChannel> EnsureChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null && _channel.IsOpen)
        {
            return _channel;
        }

        await _initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_channel is not null && _channel.IsOpen)
            {
                return _channel;
            }

            if (_connection is null || !_connection.IsOpen)
            {
                var factory = new ConnectionFactory
                {
                    HostName = _options.HostName,
                    Port = _options.Port,
                    VirtualHost = _options.VirtualHost,
                    UserName = _options.UserName,
                    Password = _options.Password,
                };

                _connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            }

#pragma warning disable CA2016 // Forward cancellation token - CreateChannelAsync doesn't support cancellation in this version
            _channel = await _connection.CreateChannelAsync().ConfigureAwait(false);
#pragma warning restore CA2016

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
    private string ResolveRoutingKey(OutboxMessage message)
    {
        if (_options.RoutingKeyResolver is not null)
        {
            return _options.RoutingKeyResolver(message);
        }

        return _options.RoutingKey;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _channel?.Dispose();
        _connection?.Dispose();
        _initializationLock.Dispose();
        _disposed = true;
    }
}
