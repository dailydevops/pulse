namespace NetEvolve.Pulse.Internals;

/// <summary>
/// Adapter interface for RabbitMQ connection operations.
/// </summary>
internal interface IRabbitMqConnectionAdapter
{
    /// <summary>
    /// Gets a value indicating whether the connection is open.
    /// </summary>
    bool IsOpen { get; }

    /// <summary>
    /// Creates a new channel asynchronously.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, containing the created channel adapter.</returns>
    Task<IRabbitMqChannelAdapter> CreateChannelAsync(CancellationToken cancellationToken = default);
}
