namespace NetEvolve.Pulse;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using NetEvolve.Pulse.Internals;

/// <summary>
/// Provides extension methods for registering the cache invalidation interceptor
/// with the Pulse mediator.
/// </summary>
/// <seealso cref="IInvalidatingCommand{TResponse}"/>
public static class CacheInvalidationMediatorBuilderExtensions
{
    /// <summary>
    /// Registers the cache invalidation interceptor for commands.
    /// Commands implementing <see cref="IInvalidatingCommand{TResponse}"/> evict the cached results of
    /// their declared query types after the command handler completes successfully.
    /// </summary>
    /// <param name="builder">The mediator builder.</param>
    /// <returns>The builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method must be called AFTER <c>AddQueryCaching()</c>, so that the cache key registry
    /// already tracks the keys produced by the query caching interceptor when invalidation runs.
    /// <para><strong>Limitations:</strong></para>
    /// <list type="bullet">
    /// <item><description>The cache key registry is in-memory and local to the process. Entries that other instances cached in the shared <c>IDistributedCache</c>, or that were cached before the process restarted, are not evicted. Configure an expiry so that they are removed eventually.</description></item>
    /// <item><description>Cache-aside race: a query that reads the data before the command commits and writes its result after the eviction leaves a stale entry until it expires.</description></item>
    /// <item><description>The registry holds each distinct cache key once per query type. Keys of expired entries are removed only when an invalidation of their query type runs.</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(c =&gt; c.AddQueryCaching().AddCacheInvalidation());
    /// </code>
    /// </example>
    public static IMediatorBuilder AddCacheInvalidation(this IMediatorBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton<ICacheKeyRegistry, InMemoryCacheKeyRegistry>();

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton(typeof(IRequestInterceptor<,>), typeof(CacheInvalidationInterceptor<,>))
        );

        return builder;
    }
}
