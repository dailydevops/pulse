namespace NetEvolve.Pulse;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Caching;
using NetEvolve.Pulse.Interceptors;

/// <summary>
/// Provides extension methods for registering the distributed cache query interceptor
/// with the Pulse mediator.
/// </summary>
/// <seealso cref="ICacheableQuery{TResponse}"/>
/// <seealso cref="QueryCachingOptions"/>
public static class QueryCachingExtensions
{
    /// <summary>
    /// Registers the distributed cache interceptor for queries.
    /// Queries implementing <see cref="ICacheableQuery{TResponse}"/> are transparently served from
    /// <c>IDistributedCache</c> when a cache entry exists, or stored after a cache miss.
    /// </summary>
    /// <param name="builder">The mediator builder.</param>
    /// <param name="configure">
    /// An optional delegate that configures <see cref="QueryCachingOptions"/>.
    /// When <see langword="null"/>, default options are used.
    /// </param>
    /// <returns>The builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    public static IMediatorBuilder AddQueryCaching(
        this IMediatorBuilder builder,
        Action<QueryCachingOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder.Services.AddOptions<QueryCachingOptions>();
        if (configure is not null)
        {
            _ = builder.Services.Configure(configure);
        }

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Scoped(typeof(IRequestInterceptor<,>), typeof(DistributedCacheQueryInterceptor<,>))
        );

        return builder;
    }
}
