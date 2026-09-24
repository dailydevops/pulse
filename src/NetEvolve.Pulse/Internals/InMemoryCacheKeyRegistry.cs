namespace NetEvolve.Pulse.Internals;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

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
