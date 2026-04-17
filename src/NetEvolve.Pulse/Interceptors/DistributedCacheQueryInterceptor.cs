namespace NetEvolve.Pulse.Interceptors;

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Caching;

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
/// <para><strong>No Cache Registered:</strong></para>
/// When <see cref="IDistributedCache"/> is not registered in the DI container, the interceptor
/// falls through to the handler without error.
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

        var response = await handler(request, cancellationToken).ConfigureAwait(false);

        if (response is not null)
        {
            var serialized = _payloadSerializer.SerializeToBytes(response);
            var entryOptions = GetCacheEntryOptions(cacheableQuery);
            await cache.SetAsync(cacheKey, serialized, entryOptions, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

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
