namespace NetEvolve.Pulse.Internals;

using System;
using System.Collections.Generic;

/// <summary>
/// Tracks the distributed cache keys produced for each query type, so that they can be looked up and
/// removed when a related command invalidates that type's cached results.
/// </summary>
internal interface ICacheKeyRegistry
{
    /// <summary>
    /// Registers a cache key as belonging to the specified query type.
    /// </summary>
    /// <param name="queryType">The query type the cache key was produced for.</param>
    /// <param name="cacheKey">The cache key to register.</param>
    void Register(Type queryType, string cacheKey);

    /// <summary>
    /// Gets a snapshot of all cache keys currently registered for the specified query type.
    /// </summary>
    /// <param name="queryType">The query type to look up.</param>
    /// <returns>
    /// A read-only list of the cache keys registered for <paramref name="queryType"/>, or an empty list
    /// when no keys are registered for that type. Never <see langword="null"/>.
    /// </returns>
    IReadOnlyList<string> GetKeysForType(Type queryType);

    /// <summary>
    /// Removes all cache keys registered for the specified query type.
    /// </summary>
    /// <param name="queryType">The query type whose registered cache keys should be removed.</param>
    void RemoveType(Type queryType);
}
