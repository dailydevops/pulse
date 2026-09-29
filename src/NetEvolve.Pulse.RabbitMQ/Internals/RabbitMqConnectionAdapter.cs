namespace NetEvolve.Pulse.Internals;

using RabbitMQ.Client;

/// <summary>
/// Adapter implementation that wraps RabbitMQ.Client IConnection.
/// </summary>
internal sealed class RabbitMqConnectionAdapter : IRabbitMqConnectionAdapter
{
    private readonly IConnection _connection;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMqConnectionAdapter"/> class.
    /// </summary>
    /// <param name="connection">The underlying RabbitMQ connection.</param>
    public RabbitMqConnectionAdapter(IConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
    }

    /// <inheritdoc />
    public bool IsOpen => _connection.IsOpen;

    /// <inheritdoc />
    /// <remarks>
    /// Every channel is created with publisher confirmations and confirmation tracking enabled, so
    /// <c>BasicPublishAsync</c> completes only after the broker acknowledged the message and throws
    /// <see cref="RabbitMQ.Client.Exceptions.PublishException"/> on a <c>basic.nack</c> or <c>basic.return</c>.
    /// No outstanding-confirms rate limiter is set, so a batch publish is never rejected or throttled by the client.
    /// </remarks>
    public async Task<IRabbitMqChannelAdapter> CreateChannelAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true,
            // No client-side limiter: in-flight confirms per channel are bounded by the outbox batch size.
            outstandingPublisherConfirmationsRateLimiter: null
        );
        var channel = await _connection.CreateChannelAsync(options, cancellationToken).ConfigureAwait(false);
        return new RabbitMqChannelAdapter(channel);
    }
}
