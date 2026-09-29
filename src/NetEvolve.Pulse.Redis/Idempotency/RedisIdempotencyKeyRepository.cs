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
/// Each key is stored in Redis with its creation timestamp as the value. When a TTL is configured,
/// a physical Redis expiry of TTL plus one hour provides automatic cleanup; without a TTL, keys are stored
/// without a Redis expiry and are only removed if the server's <c>maxmemory-policy</c> evicts non-volatile
/// keys (<c>allkeys-*</c>), which breaks duplicate detection. TTL-based logical expiry is handled by the <see cref="IdempotencyStore"/>
/// wrapper using the injected <see cref="TimeProvider"/>, which makes it testable with fake clocks.
/// <para><strong>Prerequisites:</strong></para>
/// <see cref="IConnectionMultiplexer"/> must be registered in the DI container by the caller
/// before using this provider.
/// </remarks>
internal sealed class RedisIdempotencyKeyRepository : IIdempotencyKeyRepository
{
    private const int DefaultDatabase = -1;

    /// <summary>
    /// Sets the key (ARGV[1] = UTC "O" timestamp, ARGV[3] = expiry in milliseconds or empty) unless it
    /// holds a timestamp at or after the cutoff (ARGV[2], UTC "O"). Values are compared as text, so only
    /// UTC values (ending in <c>+00:00</c>) can be refreshed. Values that are not timestamps, or carry a
    /// non-UTC offset (written by earlier versions through direct <see cref="StoreAsync"/> calls), are
    /// treated as present until their physical expiry, so a live key is never overwritten.
    /// Returns 1 when the key was set, otherwise 0.
    /// </summary>
    private const string ReserveScript = """
        local current = redis.call('GET', KEYS[1])
        if current and (not string.match(current, '^%d%d%d%d%-.*%+00:00$') or current >= ARGV[2]) then
            return 0
        end
        if ARGV[3] == '' then
            redis.call('SET', KEYS[1], ARGV[1])
        else
            redis.call('SET', KEYS[1], ARGV[1], 'PX', ARGV[3])
        end
        return 1
        """;

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
    public Task StoreAsync(
        string idempotencyKey,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        cancellationToken.ThrowIfCancellationRequested();

        return TryReserveAsync(idempotencyKey, createdAt, null, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Without <paramref name="validFrom"/> this is a plain <c>SET NX</c>. With it, a Lua script replaces an
    /// expired value and resets its expiry atomically on the server.
    /// </remarks>
    public async Task<bool> TryReserveAsync(
        string idempotencyKey,
        DateTimeOffset createdAt,
        DateTimeOffset? validFrom = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        cancellationToken.ThrowIfCancellationRequested();

        var database = _multiplexer.GetDatabase(DefaultDatabase);

        // Physical expiry is TTL + 1h headroom; a null TTL means "never expire", so no expiry is set.
        // Logical expiry is handled by the IdempotencyStore wrapper via TimeProvider.
        var physicalTtl = _options.Value.TimeToLive + TimeSpan.FromHours(1);

        var key = GetPrefixedKey(idempotencyKey);
        var timestamp = FormatTimestamp(createdAt);

        cancellationToken.ThrowIfCancellationRequested();

        if (!validFrom.HasValue)
        {
            // Returns true when the key was set; false when the key already existed.
            return await database.StringSetAsync(key, timestamp, physicalTtl, When.NotExists).ConfigureAwait(false);
        }

        var expiry = physicalTtl.HasValue
            ? ((long)physicalTtl.Value.TotalMilliseconds).ToString(CultureInfo.InvariantCulture)
            : string.Empty;

        var result = await database
            .ScriptEvaluateAsync(ReserveScript, [key], [timestamp, FormatTimestamp(validFrom.Value), expiry])
            .ConfigureAwait(false);

        return (long)result == 1;
    }

    /// <summary>
    /// Formats a timestamp as UTC round-trip text, so that stored values compare lexicographically.
    /// </summary>
    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private string GetPrefixedKey(string idempotencyKey) =>
        $"{_options.Value.Schema}:{_options.Value.TableName}:{idempotencyKey}";
}
