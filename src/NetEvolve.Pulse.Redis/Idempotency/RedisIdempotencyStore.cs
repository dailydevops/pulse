namespace NetEvolve.Pulse.Redis.Idempotency;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Idempotency;
using StackExchange.Redis;

/// <summary>
/// Redis implementation of <see cref="IIdempotencyStore"/> using atomic <c>SET NX EX</c> operations.
/// </summary>
/// <remarks>
/// <para><strong>Atomicity:</strong></para>
/// Uses Redis <c>SET key value EX ttl NX</c> which atomically checks existence and sets the key
/// in a single round-trip, eliminating race conditions inherent in separate read-before-write operations.
/// <para><strong>Prerequisites:</strong></para>
/// <see cref="IConnectionMultiplexer"/> must be registered in the DI container by the caller
/// before using this provider.
/// </remarks>
internal sealed class RedisIdempotencyStore : IIdempotencyStore
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly RedisIdempotencyKeyOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisIdempotencyStore"/> class.
    /// </summary>
    /// <param name="multiplexer">The Redis connection multiplexer.</param>
    /// <param name="options">The Redis idempotency key options.</param>
    public RedisIdempotencyStore(IConnectionMultiplexer multiplexer, IOptions<RedisIdempotencyKeyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(multiplexer);
        ArgumentNullException.ThrowIfNull(options);

        _multiplexer = multiplexer;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var database = _multiplexer.GetDatabase(_options.Database);
        return await database.KeyExistsAsync($"{_options.KeyPrefix}{idempotencyKey}").ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StoreAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var database = _multiplexer.GetDatabase(_options.Database);

        // Returns true when the key was set; false when the key already existed.
        // Both outcomes are valid — no exception is thrown for duplicates.
        _ = await database
            .StringSetAsync($"{_options.KeyPrefix}{idempotencyKey}", "1", _options.TimeToLive, When.NotExists)
            .ConfigureAwait(false);
    }
}
