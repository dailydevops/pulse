namespace NetEvolve.Pulse.Extensibility.Caching;

using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Represents a query that supports distributed caching of its response.
/// Queries implementing this interface are eligible for transparent caching via
/// <c>DistributedCacheQueryInterceptor</c>.
/// </summary>
/// <typeparam name="TResponse">The type of data returned by the query.</typeparam>
/// <remarks>
/// <para><strong>Opt-In Caching:</strong></para>
/// Caching is opt-in per query type. Only queries that implement <see cref="ICacheableQuery{TResponse}"/>
/// will be cached by the interceptor. Queries that do not implement this interface always reach the handler.
/// <para><strong>Cache Key:</strong></para>
/// The <see cref="CacheKey"/> property uniquely identifies the cached entry. Ensure it is deterministic
/// and unique per logical query result (e.g., include query parameters in the key).
/// <para><strong>Expiry:</strong></para>
/// When <see cref="Expiry"/> is <see langword="null"/>, the entry is stored without an explicit expiry,
/// relying on the cache's default eviction policy. Provide a <see cref="TimeSpan"/> value to set an
/// absolute expiration relative to the time of caching.
/// </remarks>
/// <example>
/// <code>
/// public record GetCustomerByIdQuery(string CustomerId)
///     : ICacheableQuery&lt;CustomerDetailsDto&gt;
/// {
///     public string? CorrelationId { get; set; }
///     public string CacheKey =&gt; $"customer:{CustomerId}";
///     public TimeSpan? Expiry =&gt; TimeSpan.FromMinutes(5);
/// }
/// </code>
/// </example>
/// <seealso cref="IQuery{TResponse}"/>
public interface ICacheableQuery<TResponse> : IQuery<TResponse>
{
    /// <summary>
    /// Gets the key used to store and retrieve the response from the distributed cache.
    /// </summary>
    /// <remarks>
    /// The key must be unique for each distinct query result. Include relevant query parameters
    /// to avoid cache collisions between different query instances.
    /// </remarks>
    string CacheKey { get; }

    /// <summary>
    /// Gets the duration after which the cached entry expires, or <see langword="null"/> to use the cache's default.
    /// </summary>
    /// <remarks>
    /// When <see langword="null"/>, the entry is stored without an explicit absolute expiration.
    /// When a <see cref="TimeSpan"/> value is provided, it is used as the absolute expiration
    /// relative to the time of caching.
    /// </remarks>
    TimeSpan? Expiry { get; }
}
