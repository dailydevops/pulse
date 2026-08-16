namespace NetEvolve.Pulse.Interceptors;

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Caching;
using NetEvolve.Pulse.Internals;

/// <summary>
/// An interceptor that transparently caches query responses in <see cref="IDistributedCache"/>.
/// Only queries implementing <see cref="ICacheableQuery{TResponse}"/> are eligible for caching.
/// Queries that do not implement the interface always reach the handler unchanged.
/// </summary>
/// <typeparam name="TQuery">The type of query to intercept, which must implement <see cref="IQuery{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The type of response produced by the query.</typeparam>
/// <remarks>
/// <para><strong>Cache Hit:</strong></para>
/// When a cached entry is found for the query's <see cref="ICacheableQuery{TResponse}.CacheKey"/>,
/// the handler is skipped and the deserialized response is returned directly.
/// <para><strong>Cache Miss:</strong></para>
/// When no cached entry exists, the handler is invoked and the response is serialized using the
/// configured <see cref="IPayloadSerializer"/> of <see cref="DistributedCacheQueryInterceptor{TQuery, TResponse}"/>
/// and stored in the cache before being returned to the caller.
/// <para><strong>Stampede Protection:</strong></para>
/// Concurrent in-process misses for the same cache key are coordinated through striped per-key locks:
/// only one caller executes the handler while the others wait and are served from the freshly populated
/// cache. Keys may share a stripe, so unrelated misses can occasionally serialize. Cross-process
/// stampede protection (e.g. a distributed lock) is out of scope for this interceptor.
/// <para><strong>No Cache Registered:</strong></para>
/// When <see cref="IDistributedCache"/> is not registered in the DI container, the interceptor
/// falls through to the handler without error.
/// <para><strong>Cache Key Tracking:</strong></para>
/// After a cache write, the query's cache key is registered with <see cref="ICacheKeyRegistry"/>, when
/// registered in the DI container, so that related commands can later invalidate it. When the registry
/// is not registered, this is a silent no-op.
/// <para><strong>Expiry:</strong></para>
/// The effective expiry is determined by first checking <see cref="ICacheableQuery{TResponse}.Expiry"/>;
/// when it is <see langword="null"/>, <see cref="QueryCachingOptions.DefaultExpiry"/> is used as a fallback.
/// If both are <see langword="null"/> the entry is stored without an explicit expiration.
/// The resolved expiry is applied as absolute or sliding based on <see cref="QueryCachingOptions.ExpirationMode"/>.
/// <para><strong>Serialization:</strong></para>
/// Responses are serialized and deserialized using the registered <see cref="IPayloadSerializer"/>.
/// Register a custom implementation before building the service container to override the default
/// <c>System.Text.Json</c>-based serializer.
/// </remarks>
/// <seealso cref="ICacheableQuery{TResponse}"/>
/// <seealso cref="QueryCachingOptions"/>
internal sealed class DistributedCacheQueryInterceptor<TQuery, TResponse> : IQueryInterceptor<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    /// <summary>
    /// Striped per-key locks providing in-process single-flight execution on cache misses.
    /// Static so the protection spans all (scoped) interceptor instances of this closed generic type.
    /// </summary>
    private static readonly SemaphoreSlim[] KeyLocks = CreateKeyLocks();

    private readonly IServiceProvider _serviceProvider;
    private readonly QueryCachingOptions _options;
    private readonly IPayloadSerializer _payloadSerializer;

    /// <summary>
    /// Initializes a new instance of the <see cref="DistributedCacheQueryInterceptor{TQuery, TResponse}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve <see cref="IDistributedCache"/>.</param>
    /// <param name="options">The caching options.</param>
    /// <param name="payloadSerializer">The payload serializer for cache value serialization.</param>
    public DistributedCacheQueryInterceptor(
        IServiceProvider serviceProvider,
        IOptions<QueryCachingOptions> options,
        IPayloadSerializer payloadSerializer
    )
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(payloadSerializer);

        _serviceProvider = serviceProvider;
        _options = options.Value;
        _payloadSerializer = payloadSerializer;
    }

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(
        TQuery request,
        Func<TQuery, CancellationToken, Task<TResponse>> handler,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Only cacheable queries are eligible
        if (request is not ICacheableQuery<TResponse> cacheableQuery)
        {
            return await handler(request, cancellationToken).ConfigureAwait(false);
        }

        // Fall through when IDistributedCache is not registered
        var cache = _serviceProvider.GetService<IDistributedCache>();
        if (cache is null)
        {
            return await handler(request, cancellationToken).ConfigureAwait(false);
        }

        var cacheKey = cacheableQuery.CacheKey;

        var cachedBytes = await cache.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        if (cachedBytes is not null)
        {
            var cached = _payloadSerializer.Deserialize<TResponse>(cachedBytes);
            if (cached is not null)
            {
                return cached;
            }

            await cache.RemoveAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        }

        var keyLock = KeyLocks[GetKeyLockIndex(cacheKey)];
        await keyLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check after acquiring the lock: a concurrent in-process caller may have
            // populated the cache while this caller was waiting.
            cachedBytes = await cache.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
            if (cachedBytes is not null)
            {
                var cached = _payloadSerializer.Deserialize<TResponse>(cachedBytes);
                if (cached is not null)
                {
                    return cached;
                }

                await cache.RemoveAsync(cacheKey, cancellationToken).ConfigureAwait(false);
            }

            var response = await handler(request, cancellationToken).ConfigureAwait(false);

            if (response is not null)
            {
                var serialized = _payloadSerializer.SerializeToBytes(response);
                var entryOptions = GetCacheEntryOptions(cacheableQuery);
                await cache.SetAsync(cacheKey, serialized, entryOptions, cancellationToken).ConfigureAwait(false);

                // Track the cache key for later invalidation, when a registry is registered
                var registry = _serviceProvider.GetService<ICacheKeyRegistry>();
                registry?.Register(typeof(TQuery), cacheKey);
            }

            return response;
        }
        finally
        {
            _ = keyLock.Release();
        }
    }

    /// <summary>
    /// Creates the fixed set of stripe locks used for in-process single-flight coordination.
    /// </summary>
    /// <returns>An array of single-count <see cref="SemaphoreSlim"/> instances.</returns>
    private static SemaphoreSlim[] CreateKeyLocks()
    {
        var locks = new SemaphoreSlim[32];
        for (var i = 0; i < locks.Length; i++)
        {
            locks[i] = new SemaphoreSlim(1, 1);
        }

        return locks;
    }

    /// <summary>
    /// Maps a cache key to its stripe lock index.
    /// </summary>
    /// <param name="cacheKey">The cache key to map.</param>
    /// <returns>The index of the stripe lock responsible for the key.</returns>
    private static uint GetKeyLockIndex(string cacheKey) =>
        (uint)StringComparer.Ordinal.GetHashCode(cacheKey) % (uint)KeyLocks.Length;

    /// <summary>
    /// Determines the appropriate cache entry options based on the query's expiry and the configured expiration mode.
    /// </summary>
    /// <param name="cacheableQuery">The cacheable query.</param>
    /// <returns>The cache entry options.</returns>
    private DistributedCacheEntryOptions GetCacheEntryOptions(ICacheableQuery<TResponse> cacheableQuery)
    {
        var entryOptions = new DistributedCacheEntryOptions();

        var expiry = cacheableQuery.Expiry ?? _options.DefaultExpiry;
        if (expiry.HasValue)
        {
            if (_options.ExpirationMode == CacheExpirationMode.Sliding)
            {
                entryOptions.SlidingExpiration = expiry;
            }
            else
            {
                entryOptions.AbsoluteExpirationRelativeToNow = expiry;
            }
        }

        return entryOptions;
    }
}
