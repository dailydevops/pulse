namespace NetEvolve.Pulse.Idempotency;

using System;

/// <summary>
/// Configuration options for the Redis idempotency store.
/// </summary>
public sealed class RedisIdempotencyKeyOptions
{
    /// <summary>
    /// Gets or sets the key prefix applied to all idempotency keys stored in Redis.
    /// Default: <c>"pulse:idempotency:"</c>.
    /// </summary>
    /// <remarks>
    /// The key stored in Redis will be <c>{KeyPrefix}{idempotencyKey}</c>.
    /// </remarks>
    public string KeyPrefix { get; set; } = "pulse:idempotency:";

    /// <summary>
    /// Gets or sets the time-to-live applied to idempotency keys when stored.
    /// Default: 24 hours.
    /// </summary>
    /// <remarks>
    /// Redis will automatically expire keys after this duration using the built-in key TTL mechanism.
    /// Must be greater than <see cref="TimeSpan.Zero"/>.
    /// </remarks>
    public TimeSpan TimeToLive { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Gets or sets the Redis database index to use. Use <c>-1</c> to select the default database.
    /// Default: <c>-1</c>.
    /// </summary>
    public int Database { get; set; } = -1;
}
