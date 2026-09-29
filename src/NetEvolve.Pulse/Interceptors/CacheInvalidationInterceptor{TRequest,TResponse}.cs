namespace NetEvolve.Pulse.Interceptors;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Internals;

/// <summary>
/// Request interceptor that evicts cached query results for commands implementing
/// <see cref="IInvalidatingCommand{TResponse}"/> after the handler completes successfully.
/// </summary>
/// <typeparam name="TRequest">The type of request being intercepted.</typeparam>
/// <typeparam name="TResponse">The type of response produced by the request.</typeparam>
/// <remarks>
/// <para><strong>Behavior:</strong></para>
/// <list type="number">
/// <item><description>If the request does not implement <see cref="IInvalidatingCommand{TResponse}"/>, the interceptor passes through without any cache interaction.</description></item>
/// <item><description>The handler is always invoked first; if it throws, the exception propagates and no cache eviction is performed.</description></item>
/// <item><description>If <see cref="IDistributedCache"/> is not registered in the DI container, the interceptor passes through without any cache interaction.</description></item>
/// <item><description>Otherwise, after a successful handler call, for each type in <see cref="IInvalidatingCommand{TResponse}.InvalidatedQueryTypes"/> the cache keys tracked by <see cref="ICacheKeyRegistry"/> are atomically taken from the registry and removed from the cache. Keys registered while the eviction runs are kept for the next invalidation. If an eviction fails, the keys not yet evicted are registered again and the exception propagates.</description></item>
/// </list>
/// <para><strong>Limitations:</strong></para>
/// <list type="bullet">
/// <item><description>The registry is local to the process. Entries that other instances cached in the shared <see cref="IDistributedCache"/>, or that were cached before the process restarted, are not evicted; they are removed only when they expire.</description></item>
/// <item><description>Cache-aside race: a query that reads the data before the command commits and writes its result after the eviction leaves a stale entry until it expires.</description></item>
/// <item><description>The registry holds each distinct cache key once per query type. Keys of expired entries are removed only when an invalidation of their query type runs.</description></item>
/// </list>
/// <para><strong>Registration:</strong></para>
/// Use <c>AddCacheInvalidation()</c> on the <see cref="IMediatorBuilder"/> to register this interceptor.
/// </remarks>
/// <seealso cref="IInvalidatingCommand{TResponse}"/>
/// <seealso cref="ICacheKeyRegistry"/>
internal sealed class CacheInvalidationInterceptor<TRequest, TResponse> : IRequestInterceptor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ICacheKeyRegistry _cacheKeyRegistry;

    /// <summary>
    /// Initializes a new instance of the <see cref="CacheInvalidationInterceptor{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve <see cref="IDistributedCache"/>.</param>
    /// <param name="cacheKeyRegistry">The registry tracking cache keys produced per query type.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="serviceProvider"/> or <paramref name="cacheKeyRegistry"/> is <see langword="null"/>.</exception>
    public CacheInvalidationInterceptor(IServiceProvider serviceProvider, ICacheKeyRegistry cacheKeyRegistry)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(cacheKeyRegistry);

        _serviceProvider = serviceProvider;
        _cacheKeyRegistry = cacheKeyRegistry;
    }

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResponse>> handler,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(handler);

        if (request is not IInvalidatingCommand<TResponse> invalidatingCommand)
        {
            return await handler(request, cancellationToken).ConfigureAwait(false);
        }

        var response = await handler(request, cancellationToken).ConfigureAwait(false);

        var cache = _serviceProvider.GetService<IDistributedCache>();
        if (cache is null)
        {
            return response;
        }

        foreach (var queryType in invalidatingCommand.InvalidatedQueryTypes)
        {
            var keys = _cacheKeyRegistry.RemoveType(queryType);
            for (var i = 0; i < keys.Count; i++)
            {
                try
                {
                    await cache.RemoveAsync(keys[i], cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Keep the keys that were not evicted, so that a later invalidation can still evict them.
                    for (var j = i; j < keys.Count; j++)
                    {
                        _cacheKeyRegistry.Register(queryType, keys[j]);
                    }

                    throw;
                }
            }
        }

        return response;
    }
}
