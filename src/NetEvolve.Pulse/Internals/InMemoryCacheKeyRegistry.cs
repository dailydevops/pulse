namespace NetEvolve.Pulse.Internals;

using System;
using System.Collections.Generic;

/// <summary>
/// Thread-safe, in-memory implementation of <see cref="ICacheKeyRegistry"/> that stores the cache keys of
/// each query type as a set, guarded by a single lock.
/// </summary>
/// <remarks>
/// The registry is local to the process and holds each distinct cache key once per query type. Keys are
/// removed only by <see cref="RemoveType(Type)"/>, so a key stays registered after its cache entry has
/// expired until the next invalidation of that query type.
/// </remarks>
internal sealed class InMemoryCacheKeyRegistry : ICacheKeyRegistry
{
    // ponytail: one global lock; Register runs only on cache-miss writes. Switch to per-type locks if contention shows up.
    private readonly object _sync = new();
    private readonly Dictionary<Type, HashSet<string>> _keysByQueryType = [];

    /// <inheritdoc />
    public void Register(Type queryType, string cacheKey)
    {
        ArgumentNullException.ThrowIfNull(queryType);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);

        lock (_sync)
        {
            if (!_keysByQueryType.TryGetValue(queryType, out var keys))
            {
                keys = new HashSet<string>(StringComparer.Ordinal);
                _keysByQueryType[queryType] = keys;
            }

            _ = keys.Add(cacheKey);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetKeysForType(Type queryType)
    {
        ArgumentNullException.ThrowIfNull(queryType);

        lock (_sync)
        {
            return _keysByQueryType.TryGetValue(queryType, out var keys) ? [.. keys] : [];
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> RemoveType(Type queryType)
    {
        ArgumentNullException.ThrowIfNull(queryType);

        lock (_sync)
        {
            return _keysByQueryType.Remove(queryType, out var keys) ? [.. keys] : [];
        }
    }
}
