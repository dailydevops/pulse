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
/// <item><description>Otherwise, after a successful handler call, for each type in <see cref="IInvalidatingCommand{TResponse}.InvalidatedQueryTypes"/> the cache keys tracked by <see cref="ICacheKeyRegistry"/> are removed from the cache and the registry entries for that type are cleared.</description></item>
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
            var keys = _cacheKeyRegistry.GetKeysForType(queryType);
            foreach (var key in keys)
            {
                await cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            }

            _cacheKeyRegistry.RemoveType(queryType);
        }

        return response;
    }
}
