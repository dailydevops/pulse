namespace NetEvolve.Pulse.Outbox;

using System.Linq;
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
/// This transport uses an injected <see cref="IRabbitMqChannelPool"/> to rent channels on
/// demand. The connection lifetime is managed externally via dependency injection.
/// <para><strong>Delivery Guarantees:</strong></para>
/// Channels are created with publisher confirmations and confirmation tracking enabled, and
/// every message is published as persistent (delivery mode 2) with <c>mandatory: true</c>.
/// A send therefore completes only after the broker acknowledged the message, and throws when
/// the broker nacks it, returns it as unroutable, or closes the channel (for example because
/// the exchange does not exist). The outbox then keeps the message pending for retry.
/// <para><strong>Channel Pooling:</strong></para>
/// Each <see cref="SendAsync"/> call rents its own channel from the pool for the duration of
/// the publish and returns it afterwards, so concurrent sends wait for their confirmations on
/// separate channels. <see cref="SendBatchAsync"/> rents a single channel for the whole batch.
/// <para><strong>Routing Key Resolution:</strong></para>
/// Each message is published with a routing key resolved by <see cref="ITopicNameResolver"/>.
/// By default, the simple class name of the event type is used (e.g., <c>"OrderCreated"</c>).
/// <para><strong>Payload:</strong></para>
/// The raw JSON payload from <see cref="OutboxMessage.Payload"/> is published as the message body.
/// <para><strong>Health Checks:</strong></para>
/// The <see cref="IsHealthyAsync"/> method delegates to the channel pool's own health check.
/// </remarks>
internal sealed class RabbitMqMessageTransport : IMessageTransport, IDisposable
{
    /// <summary>The resolved transport options controlling the RabbitMQ connection and exchange settings.</summary>
    private readonly RabbitMqTransportOptions _options;

    /// <summary>The topic name resolver used to determine the routing key from an outbox message.</summary>
    private readonly ITopicNameResolver _topicNameResolver;

    /// <summary>The RabbitMQ channel pool channels are rented from and returned to.</summary>
    private readonly IRabbitMqChannelPool _channelPool;

    /// <summary>
    /// Disposal sentinel handled via <see cref="Interlocked.Exchange(ref int, int)"/> so that
    /// concurrent <see cref="Dispose"/> calls observe a single winning thread.
    /// </summary>
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMqMessageTransport"/> class.
    /// </summary>
    /// <param name="channelPool">The RabbitMQ channel pool used to rent channels for publishing.</param>
    /// <param name="topicNameResolver">The topic name resolver for determining routing keys from outbox messages.</param>
    /// <param name="options">The transport options.</param>
    internal RabbitMqMessageTransport(
        IRabbitMqChannelPool channelPool,
        ITopicNameResolver topicNameResolver,
        IOptions<RabbitMqTransportOptions> options
    )
    {
        ArgumentNullException.ThrowIfNull(channelPool);
        ArgumentNullException.ThrowIfNull(topicNameResolver);
        ArgumentNullException.ThrowIfNull(options);

        _channelPool = channelPool;
        _topicNameResolver = topicNameResolver;
        _options = options.Value;
    }

    /// <inheritdoc />
    /// <exception cref="ObjectDisposedException">Thrown when the transport has already been disposed.</exception>
    public async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(message);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var channel = await _channelPool.RentAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await PublishAsync(channel, message, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _channelPool.Return(channel);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Rents a single channel for the whole batch, starts the publish of every message on it and
    /// then awaits all publisher confirmations together, following the batch pattern of the
    /// RabbitMQ.Client publisher confirms sample. The channel is returned to the pool only after
    /// every publish has settled; the first failure (nack, return or channel close) is rethrown.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when the transport has already been disposed.</exception>
    public async Task SendBatchAsync(IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(messages);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var channel = await _channelPool.RentAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.WhenAll(messages.Select(message => PublishAsync(channel, message, cancellationToken)))
                .ConfigureAwait(false);
        }
        finally
        {
            _channelPool.Return(channel);
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
        cancellationToken.ThrowIfCancellationRequested();

        var routingKey = ResolveRoutingKey(message);
        var body = Encoding.UTF8.GetBytes(message.Payload);

        var properties = new BasicProperties
        {
            MessageId = message.Id.ToString(),
            CorrelationId = message.CorrelationId,
            ContentType = "application/json",
            Persistent = true,
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
                mandatory: true,
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
        cancellationToken.ThrowIfCancellationRequested();

        if (Volatile.Read(ref _disposed) != 0)
        {
            return Task.FromResult(false);
        }

        try
        {
            return _channelPool.IsHealthyAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(false);
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
    /// callers (including concurrent ones) are no-ops. The transport itself no longer owns any
    /// channel: the pool is registered and disposed as part of the DI container's lifetime, so
    /// there is nothing further to release here beyond flipping the sentinel so that in-flight
    /// and subsequent calls observe <see cref="ObjectDisposedException"/> / a false health check.
    /// </remarks>
    public void Dispose() => _ = Interlocked.Exchange(ref _disposed, 1);
}
