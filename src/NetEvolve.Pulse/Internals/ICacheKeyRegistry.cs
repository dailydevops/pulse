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
    /// Atomically removes and returns all cache keys registered for the specified query type.
    /// </summary>
    /// <param name="queryType">The query type whose registered cache keys should be removed.</param>
    /// <returns>
    /// The cache keys that were registered for <paramref name="queryType"/>, or an empty list when none were
    /// registered. Never <see langword="null"/>. Keys registered after this call are kept for the next call.
    /// </returns>
    IReadOnlyList<string> RemoveType(Type queryType);
}
