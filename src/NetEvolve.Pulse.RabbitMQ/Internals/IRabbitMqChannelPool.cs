namespace NetEvolve.Pulse.Internals;

/// <summary>
/// A pool of RabbitMQ channels used to avoid serializing all publishes through a single
/// shared, non-thread-safe channel.
/// </summary>
internal interface IRabbitMqChannelPool
{
    /// <summary>
    /// Rents a channel from the pool, creating a new one if none are idle. Blocks
    /// asynchronously until a slot becomes available when the pool is at capacity.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, containing a rented channel adapter.</returns>
    ValueTask<IRabbitMqChannelAdapter> RentAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns a previously rented channel back to the pool. An open channel is kept for
    /// reuse; a closed channel is disposed instead. Either way, the corresponding rental
    /// slot is released exactly once.
    /// </summary>
    /// <param name="channel">The channel to return.</param>
    void Return(IRabbitMqChannelAdapter channel);

    /// <summary>
    /// Checks whether the pool (and the underlying connection it creates channels from) is healthy.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, containing a value indicating whether the pool is healthy.</returns>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken);
}
