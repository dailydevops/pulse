namespace NetEvolve.Pulse.Internals;

using RabbitMQ.Client;

/// <summary>
/// Adapter implementation that wraps RabbitMQ.Client IChannel.
/// </summary>
internal sealed class RabbitMqChannelAdapter : IRabbitMqChannelAdapter
{
    private readonly IChannel _channel;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMqChannelAdapter"/> class.
    /// </summary>
    /// <param name="channel">The underlying RabbitMQ channel.</param>
    public RabbitMqChannelAdapter(IChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        _channel = channel;
    }

    /// <inheritdoc />
    public bool IsOpen => _channel.IsOpen;

    /// <inheritdoc />
    public ValueTask BasicPublishAsync<TProperties>(
        string exchange,
        string routingKey,
        bool mandatory,
        TProperties basicProperties,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default
    )
        where TProperties : IReadOnlyBasicProperties, IAmqpHeader =>
        _channel.BasicPublishAsync(exchange, routingKey, mandatory, basicProperties, body, cancellationToken);

    /// <inheritdoc />
    public void Dispose() => _channel.Dispose();
}
