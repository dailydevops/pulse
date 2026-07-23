namespace NetEvolve.Pulse.Outbox;

using System.Text;
using System.Threading;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Internals;
using RabbitMQ.Client;

/// <summary>
/// Message transport that publishes outbox messages to RabbitMQ exchanges.
/// </summary>
/// <remarks>
/// <para><strong>Connection Management:</strong></para>
/// This transport uses an injected connection adapter and creates channels on demand.
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

    /// <summary>The RabbitMQ connection adapter.</summary>
    private readonly IRabbitMqConnectionAdapter _connectionAdapter;

    /// <summary>Lazy-initialized RabbitMQ channel for publishing.</summary>
    private IRabbitMqChannelAdapter? _channel;

    /// <summary>Semaphore for thread-safe channel initialization.</summary>
    private readonly SemaphoreSlim _initializationLock = new(1, 1);

    /// <summary>
    /// Disposal sentinel handled via <see cref="Interlocked.Exchange(ref int, int)"/> so that
    /// concurrent <see cref="Dispose"/> calls observe a single winning thread. Storing this as a
    /// plain <c>bool</c> would leave a TOCTOU window between the early-exit check and the
    /// teardown work, allowing the underlying channel adapter to be disposed twice.
    /// </summary>
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMqMessageTransport"/> class.
    /// </summary>
    /// <param name="connectionAdapter">The RabbitMQ connection adapter.</param>
    /// <param name="topicNameResolver">The topic name resolver for determining routing keys from outbox messages.</param>
    /// <param name="options">The transport options.</param>
    internal RabbitMqMessageTransport(
        IRabbitMqConnectionAdapter connectionAdapter,
        ITopicNameResolver topicNameResolver,
        IOptions<RabbitMqTransportOptions> options
    )
    {
        ArgumentNullException.ThrowIfNull(connectionAdapter);
        ArgumentNullException.ThrowIfNull(topicNameResolver);
        ArgumentNullException.ThrowIfNull(options);

        _connectionAdapter = connectionAdapter;
        _topicNameResolver = topicNameResolver;
        _options = options.Value;
    }

    /// <inheritdoc />
    /// <exception cref="ObjectDisposedException">Thrown when the transport has already been disposed.</exception>
    public async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var channel = await EnsureChannelAsync(cancellationToken).ConfigureAwait(false);
        await PublishAsync(channel, message, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Overridden to publish all messages sequentially on the same channel. RabbitMQ.Client's
    /// <see cref="IChannel"/> is NOT thread-safe for concurrent publish calls, so the default
    /// parallel <c>Parallel.ForEachAsync</c> implementation provided by the interface must not
    /// be used on this transport.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when the transport has already been disposed.</exception>
    public async Task SendBatchAsync(IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var channel = await EnsureChannelAsync(cancellationToken).ConfigureAwait(false);

        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await PublishAsync(channel, message, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Publishes a single outbox message to the configured RabbitMQ exchange using the supplied channel.
    /// </summary>
    /// <param name="channel">The channel used to publish the message.</param>
    /// <param name="message">The outbox message to publish.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    private async Task PublishAsync(
        IRabbitMqChannelAdapter channel,
        OutboxMessage message,
        CancellationToken cancellationToken
    )
    {
        var routingKey = ResolveRoutingKey(message);
        var body = Encoding.UTF8.GetBytes(message.Payload);

        var properties = new BasicProperties
        {
            MessageId = message.Id.ToString(),
            CorrelationId = message.CorrelationId,
            ContentType = "application/json",
            Timestamp = new AmqpTimestamp(message.CreatedAt.ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["eventType"] = message.EventType.ToOutboxEventTypeName(),
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
    /// <remarks>
    /// Returns <see langword="false"/> when the transport has been disposed instead of throwing,
    /// because health probes commonly run during shutdown and should report unhealthy rather than fail.
    /// </remarks>
    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return Task.FromResult(false);
        }

        try
        {
            if (_connectionAdapter?.IsOpen != true || _channel?.IsOpen != true)
            {
                return Task.FromResult(false);
            }

            // Perform a lightweight check by verifying channel is still open
            // RabbitMQ client maintains the connection state internally
            return Task.FromResult(_channel.IsOpen);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Ensures that a channel is available, creating it if necessary.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The initialized channel.</returns>
    private async Task<IRabbitMqChannelAdapter> EnsureChannelAsync(CancellationToken cancellationToken)
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

            // Dispose previously-acquired (now closed) channel to avoid leaking the
            // underlying RabbitMQ.Client IChannel handle before replacing the reference.
            _channel?.Dispose();

            _channel = await _connectionAdapter.CreateChannelAsync(cancellationToken).ConfigureAwait(false);

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
    /// <remarks>
    /// Disposal is single-shot under concurrency: the first thread to flip <c>_disposed</c> via
    /// <see cref="Interlocked.Exchange(ref int, int)"/> performs the teardown; all subsequent
    /// callers (including concurrent ones) are no-ops. This prevents double-disposal of the
    /// underlying channel adapter when shutdown overlaps with another <c>Dispose()</c> call.
    /// </remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _channel?.Dispose();
        _initializationLock.Dispose();
    }
}
