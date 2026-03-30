namespace NetEvolve.Pulse.Internals;

using RabbitMQ.Client;

/// <summary>
/// Adapter interface for RabbitMQ channel operations.
/// </summary>
internal interface IRabbitMqChannelAdapter : IDisposable
{
    /// <summary>
    /// Gets a value indicating whether the channel is open.
    /// </summary>
    bool IsOpen { get; }

    /// <summary>
    /// Publishes a message asynchronously.
    /// </summary>
    /// <typeparam name="TProperties">The type of basic properties.</typeparam>
    /// <param name="exchange">The exchange to publish to.</param>
    /// <param name="routingKey">The routing key.</param>
    /// <param name="mandatory">Whether the message is mandatory.</param>
    /// <param name="basicProperties">The message properties.</param>
    /// <param name="body">The message body.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask BasicPublishAsync<TProperties>(
        string exchange,
        string routingKey,
        bool mandatory,
        TProperties basicProperties,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default
    )
        where TProperties : IReadOnlyBasicProperties, IAmqpHeader;
}
