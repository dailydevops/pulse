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
/// <para><strong>Reservation:</strong></para>
/// <see cref="TryStoreAsync"/> reserves a key atomically with <c>SET NX</c>. A key that is logically expired but
/// still physically present is replaced through a compare-and-set transaction, so of several concurrent
/// reservations for the same key exactly one succeeds.
/// <para><strong>Prerequisites:</strong></para>
/// <see cref="IConnectionMultiplexer"/> must be registered in the DI container by the caller
/// before using this provider.
/// </remarks>
internal sealed class RedisIdempotencyKeyRepository : IIdempotencyKeyRepository
{
    private const int DefaultDatabase = -1;

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

        // An unparseable value (e.g. legacy entry) is not expired and therefore treated as present.
        return !IsExpired(value, validFrom.Value);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Reserves the key with an atomic <c>SET NX</c>. When the key already exists and <paramref name="validFrom"/>
    /// is set, a stored timestamp older than <paramref name="validFrom"/> is replaced through a transaction that
    /// only commits while the key still holds the observed value, so of several concurrent calls at most one
    /// returns <see langword="true"/>.
    /// </remarks>
    public async Task<bool> TryStoreAsync(
        string idempotencyKey,
        DateTimeOffset createdAt,
        DateTimeOffset? validFrom = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var database = _multiplexer.GetDatabase(DefaultDatabase);
        var key = GetPrefixedKey(idempotencyKey);
        var timestamp = createdAt.ToString("O", CultureInfo.InvariantCulture);
        var physicalTtl = GetPhysicalTimeToLive();

        if (await database.StringSetAsync(key, timestamp, physicalTtl, When.NotExists).ConfigureAwait(false))
        {
            return true;
        }

        // Without a TTL an existing key never expires logically, so it is never reservable again.
        if (!validFrom.HasValue)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var existing = await database.StringGetAsync(key).ConfigureAwait(false);

        if (!existing.HasValue)
        {
            // The key expired physically between SET NX and GET; try the plain reservation once more.
            return await database.StringSetAsync(key, timestamp, physicalTtl, When.NotExists).ConfigureAwait(false);
        }

        if (!IsExpired(existing, validFrom.Value))
        {
            return false;
        }

        // Compare-and-set: replace the expired value only if no concurrent call changed it meanwhile.
        var transaction = database.CreateTransaction();
        _ = transaction.AddCondition(Condition.StringEqual(key, existing));
        _ = transaction.StringSetAsync(key, timestamp, physicalTtl, When.Always);

        return await transaction.ExecuteAsync().ConfigureAwait(false);
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

        var physicalTtl = GetPhysicalTimeToLive();

        var timestamp = createdAt.ToString("O", CultureInfo.InvariantCulture);

        // Returns true when the key was set; false when the key already existed.
        // Both outcomes are valid — no exception is thrown for duplicates.
        cancellationToken.ThrowIfCancellationRequested();

        _ = await database
            .StringSetAsync(GetPrefixedKey(idempotencyKey), timestamp, physicalTtl, When.NotExists)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the physical Redis expiry: TTL plus one hour headroom, or <see langword="null"/> (no expiry)
    /// when no TTL is configured. Logical expiry is handled by the IdempotencyStore wrapper via TimeProvider.
    /// </summary>
    private TimeSpan? GetPhysicalTimeToLive() => _options.Value.TimeToLive + TimeSpan.FromHours(1);

    private static bool IsExpired(RedisValue value, DateTimeOffset validFrom) =>
        DateTimeOffset.TryParse(
            value.ToString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var createdAt
        )
        && createdAt < validFrom;

    private string GetPrefixedKey(string idempotencyKey) =>
        $"{_options.Value.Schema}:{_options.Value.TableName}:{idempotencyKey}";
}
