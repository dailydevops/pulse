namespace NetEvolve.Pulse.Outbox;

using System.Collections.Concurrent;

/// <summary>
/// Resolves and caches <see cref="Type"/> lookups from assembly-qualified event type names
/// stored in the outbox table, avoiding repeated <see cref="Type.GetType(string)"/> parsing
/// and loader lookups on every row mapped in the polling hot path.
/// </summary>
internal static class OutboxEventTypeResolver
{
    private static readonly ConcurrentDictionary<string, Type> Cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Resolves <paramref name="typeName"/> to a <see cref="Type"/>, memoizing successful
    /// resolutions in a process-wide cache keyed by the assembly-qualified name.
    /// </summary>
    /// <param name="typeName">The assembly-qualified type name read from storage.</param>
    /// <returns>The resolved <see cref="Type"/>, or <see langword="null"/> if it cannot be resolved.</returns>
    public static Type? Resolve(string typeName)
    {
        if (Cache.TryGetValue(typeName, out var cached))
        {
            return cached;
        }

        var resolved = Type.GetType(typeName);
        if (resolved is null)
        {
            return null;
        }

        return Cache[typeName] = resolved;
    }
}
