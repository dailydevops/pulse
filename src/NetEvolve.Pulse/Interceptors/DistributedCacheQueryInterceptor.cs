namespace NetEvolve.Pulse.Interceptors;

using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;

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
/// When no cached entry exists, the handler is invoked and the response is serialized to JSON
/// and stored in the cache before being returned to the caller.
/// <para><strong>No Cache Registered:</strong></para>
/// When <see cref="IDistributedCache"/> is not registered in the DI container, the interceptor
/// falls through to the handler without error.
/// <para><strong>Expiry:</strong></para>
/// When <see cref="ICacheableQuery{TResponse}.Expiry"/> is <see langword="null"/>, the entry is stored
/// without an explicit absolute expiration. Otherwise the provided <see cref="TimeSpan"/> is used
/// as absolute expiration relative to now.
/// <para><strong>Serialization:</strong></para>
/// Responses are serialized using <c>System.Text.Json</c> with default options. Complex types with
/// custom converters, circular references, or special serialization requirements may not round-trip
/// correctly. Ensure <typeparamref name="TResponse"/> is serializable with the default options.
/// </remarks>
/// <seealso cref="ICacheableQuery{TResponse}"/>
internal sealed class DistributedCacheQueryInterceptor<TQuery, TResponse> : IQueryInterceptor<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DistributedCacheQueryInterceptor{TQuery, TResponse}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve <see cref="IDistributedCache"/>.</param>
    public DistributedCacheQueryInterceptor(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

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
            return JsonSerializer.Deserialize<TResponse>(cachedBytes)!;
        }

        var response = await handler(request, cancellationToken).ConfigureAwait(false);

        var serialized = JsonSerializer.SerializeToUtf8Bytes(response);

        var options = new DistributedCacheEntryOptions();
        if (cacheableQuery.Expiry.HasValue)
        {
            options.AbsoluteExpirationRelativeToNow = cacheableQuery.Expiry;
        }

        await cache.SetAsync(cacheKey, serialized, options, cancellationToken).ConfigureAwait(false);

        return response;
    }
}
