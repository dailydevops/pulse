namespace NetEvolve.Pulse.Extensibility.Caching;

/// <summary>
/// Specifies how cached entries created by the distributed cache interceptor should expire.
/// </summary>
/// <seealso cref="QueryCachingOptions"/>
/// <seealso cref="ICacheableQuery{TResponse}"/>
public enum CacheExpirationMode
{
    /// <summary>
    /// The cache entry expires at an absolute point in time calculated as
    /// the current instant plus <see cref="ICacheableQuery{TResponse}.Expiry"/>.
    /// This is the default mode.
    /// </summary>
    Absolute = 0,

    /// <summary>
    /// The cache entry expiry window is reset each time the entry is accessed.
    /// The entry expires after <see cref="ICacheableQuery{TResponse}.Expiry"/> elapses
    /// since the last access.
    /// </summary>
    Sliding = 1,
}
