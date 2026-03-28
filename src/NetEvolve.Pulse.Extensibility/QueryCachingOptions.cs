namespace NetEvolve.Pulse.Extensibility;

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
    /// no expiry is set regardless of this setting.
    /// </remarks>
    public CacheExpirationMode ExpirationMode { get; set; } = CacheExpirationMode.Absolute;
}
