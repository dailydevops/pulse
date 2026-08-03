namespace NetEvolve.Pulse.Internals;

using System;
using System.Collections.Concurrent;
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

/// <summary>
/// Thread-safe, in-memory implementation of <see cref="ICacheKeyRegistry"/> backed by a
/// <see cref="ConcurrentDictionary{TKey, TValue}"/> of <see cref="ConcurrentBag{T}"/> instances.
/// </summary>
internal sealed class InMemoryCacheKeyRegistry : ICacheKeyRegistry
{
    private readonly ConcurrentDictionary<Type, ConcurrentBag<string>> _keysByQueryType = new();

    /// <inheritdoc />
    public void Register(Type queryType, string cacheKey)
    {
        ArgumentNullException.ThrowIfNull(queryType);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);

        var bag = _keysByQueryType.GetOrAdd(queryType, static _ => []);
        bag.Add(cacheKey);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetKeysForType(Type queryType)
    {
        ArgumentNullException.ThrowIfNull(queryType);

        return _keysByQueryType.TryGetValue(queryType, out var bag) ? bag.ToArray() : [];
    }

    /// <inheritdoc />
    public void RemoveType(Type queryType)
    {
        ArgumentNullException.ThrowIfNull(queryType);

        _ = _keysByQueryType.TryRemove(queryType, out _);
    }
}
