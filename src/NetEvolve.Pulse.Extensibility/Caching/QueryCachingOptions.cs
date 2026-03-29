namespace NetEvolve.Pulse.Extensibility.Caching;

using System.Text.Json;

/// <summary>
/// Configuration options for the distributed query caching interceptor.
/// </summary>
/// <remarks>
/// Configure these options when calling <see cref="IMediatorConfigurator.AddQueryCaching"/>.
/// </remarks>
/// <seealso cref="IMediatorConfigurator.AddQueryCaching"/>
/// <seealso cref="ICacheableQuery{TResponse}"/>
public sealed class QueryCachingOptions
{
    /// <summary>
    /// Gets or sets the <see cref="System.Text.Json.JsonSerializerOptions"/> used when
    /// serializing responses to the cache and deserializing them back.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="JsonSerializerOptions.Default"/>.
    /// Provide custom options when your response types require non-default converters,
    /// naming policies, or other serialization settings.
    /// </remarks>
    public JsonSerializerOptions JsonSerializerOptions { get; set; } = JsonSerializerOptions.Default;

    /// <summary>
    /// Gets or sets how the <see cref="ICacheableQuery{TResponse}.Expiry"/> value is interpreted
    /// when storing entries in the distributed cache.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>
    ///   <description>
    ///     <see cref="CacheExpirationMode.Absolute"/> (default) — sets
    ///     <c>DistributedCacheEntryOptions.AbsoluteExpirationRelativeToNow</c>.
    ///   </description>
    /// </item>
    /// <item>
    ///   <description>
    ///     <see cref="CacheExpirationMode.Sliding"/> — sets
    ///     <c>DistributedCacheEntryOptions.SlidingExpiration</c>, resetting the
    ///     expiry window on each cache access.
    ///   </description>
    /// </item>
    /// </list>
    /// When <see cref="ICacheableQuery{TResponse}.Expiry"/> is <see langword="null"/>,
    /// <see cref="DefaultExpiry"/> is used if set; otherwise no expiry is applied and the
    /// cache's default eviction policy is used.
    /// </remarks>
    public CacheExpirationMode ExpirationMode { get; set; } = CacheExpirationMode.Absolute;

    /// <summary>
    /// Gets or sets the fallback expiry duration used when
    /// <see cref="ICacheableQuery{TResponse}.Expiry"/> returns <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// When <see langword="null"/> (default), entries whose query returns a <see langword="null"/>
    /// <see cref="ICacheableQuery{TResponse}.Expiry"/> are stored without an explicit expiration,
    /// relying on the cache's default eviction policy.
    /// When a <see cref="TimeSpan"/> value is provided, it is applied according to
    /// <see cref="ExpirationMode"/> in the same way as a per-query expiry would be.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config => config.AddQueryCaching(options =>
    /// {
    ///     // All queries without an explicit Expiry get a 10-minute TTL
    ///     options.DefaultExpiry = TimeSpan.FromMinutes(10);
    /// }));
    /// </code>
    /// </example>
    public TimeSpan? DefaultExpiry { get; set; }
}
