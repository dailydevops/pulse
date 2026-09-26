namespace NetEvolve.Pulse.Outbox;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Resolves and caches <see cref="Type"/> lookups from assembly-qualified event type names
/// stored in the outbox table, avoiding repeated <see cref="Type.GetType(string)"/> parsing
/// and loader lookups on every row mapped in the polling hot path.
/// </summary>
internal static class OutboxEventTypeResolver
{
    private static readonly ConcurrentDictionary<string, Type> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Resolves <paramref name="typeName"/> to a <see cref="Type"/>, memoizing successful
    /// resolutions in a process-wide cache keyed by the assembly-qualified name.
    /// </summary>
    /// <param name="typeName">The assembly-qualified type name read from storage.</param>
    /// <returns>The resolved <see cref="Type"/>, or <see langword="null"/> if it cannot be resolved.</returns>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2057:Unrecognized value passed to the parameter of method with 'DynamicallyAccessedMembersAttribute'",
        Justification = "The resolved event type is only used for its identity (grouping, naming, per-event-type options); no members are reflected on. A type that cannot be resolved in a trimmed or NativeAOT application takes the existing unresolvable-type path."
    )]
    public static Type? Resolve(string typeName)
    {
        if (_cache.TryGetValue(typeName, out var cached))
        {
            return cached;
        }

        var resolved = Type.GetType(typeName);
        if (resolved is null)
        {
            return null;
        }

        return _cache[typeName] = resolved;
    }
}
