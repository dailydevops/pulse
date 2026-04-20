namespace NetEvolve.Pulse.Idempotency;

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Idempotency;
using StackExchange.Redis;

/// <summary>
/// Redis implementation of <see cref="IIdempotencyKeyRepository"/>.
/// </summary>
/// <remarks>
/// <para><strong>Storage:</strong></para>
/// Each key is stored in Redis with its creation timestamp as the value and a physical TTL
/// for automatic cleanup. TTL-based logical expiry is handled by the <see cref="IdempotencyStore"/>
/// wrapper using the injected <see cref="TimeProvider"/>, which makes it testable with fake clocks.
/// <para><strong>Prerequisites:</strong></para>
/// <see cref="IConnectionMultiplexer"/> must be registered in the DI container by the caller
/// before using this provider.
/// </remarks>
internal sealed class RedisIdempotencyKeyRepository : IIdempotencyKeyRepository
{
    private const int DefaultDatabase = -1;

    /// <summary>Physical TTL used as a safety net for Redis key cleanup when no TTL is configured.</summary>
    private static readonly TimeSpan DefaultPhysicalTtl = TimeSpan.FromHours(24);

    private readonly IConnectionMultiplexer _multiplexer;
    private readonly IOptions<IdempotencyKeyOptions> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisIdempotencyKeyRepository"/> class.
    /// </summary>
    /// <param name="multiplexer">The Redis connection multiplexer.</param>
    /// <param name="options">The idempotency key options.</param>
    public RedisIdempotencyKeyRepository(IConnectionMultiplexer multiplexer, IOptions<IdempotencyKeyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(multiplexer);
        ArgumentNullException.ThrowIfNull(options);

        _multiplexer = multiplexer;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(
        string idempotencyKey,
        DateTimeOffset? validFrom = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        cancellationToken.ThrowIfCancellationRequested();

        var database = _multiplexer.GetDatabase(DefaultDatabase);

        cancellationToken.ThrowIfCancellationRequested();

        var value = await database.StringGetAsync(GetPrefixedKey(idempotencyKey)).ConfigureAwait(false);

        if (!value.HasValue)
        {
            return false;
        }

        if (!validFrom.HasValue)
        {
            return true;
        }

        // Parse the stored creation timestamp and check it is within the TTL window.
        if (
            DateTimeOffset.TryParse(
                value.ToString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var createdAt
            )
        )
        {
            return createdAt >= validFrom.Value;
        }

        // If the value cannot be parsed (e.g. legacy entry), treat it as present.
        return true;
    }

    /// <inheritdoc />
    public async Task StoreAsync(
        string idempotencyKey,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        cancellationToken.ThrowIfCancellationRequested();

        var database = _multiplexer.GetDatabase(DefaultDatabase);

        // Use a physical TTL
        // Logical expiry is handled by the IdempotencyStore wrapper via TimeProvider.
        var physicalTtl = _options.Value.TimeToLive.HasValue
            ? _options.Value.TimeToLive.Value + TimeSpan.FromHours(1)
            : DefaultPhysicalTtl;

        var timestamp = createdAt.ToString("O", CultureInfo.InvariantCulture);

        // Returns true when the key was set; false when the key already existed.
        // Both outcomes are valid — no exception is thrown for duplicates.
        cancellationToken.ThrowIfCancellationRequested();

        _ = await database
            .StringSetAsync(GetPrefixedKey(idempotencyKey), timestamp, physicalTtl, When.NotExists)
            .ConfigureAwait(false);
    }

    private string GetPrefixedKey(string idempotencyKey) =>
        $"{_options.Value.Schema}:{_options.Value.TableName}:{idempotencyKey}";
}
