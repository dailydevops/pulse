namespace NetEvolve.Pulse.Extensibility.Outbox;

using System.Collections.Concurrent;

/// <summary>
/// Default implementation of <see cref="ITopicNameResolver"/> that extracts the simple class name
/// from an assembly-qualified type name.
/// </summary>
/// <remarks>
/// For example, <c>"MyApp.Events.OrderCreated, MyApp, Version=1.0.0.0, ..."</c> resolves to <c>"OrderCreated"</c>.
/// </remarks>
internal sealed class DefaultTopicNameResolver : ITopicNameResolver
{
    /// <summary>Cache for resolved topic names to avoid repeated string parsing operations.</summary>
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public string Resolve(OutboxMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var eventType = message.EventType;

        // Fast path for cache hits - avoids delegate invocation
        if (_cache.TryGetValue(eventType, out var cached))
        {
            return cached;
        }

        return _cache.GetOrAdd(eventType, ExtractSimpleTypeName);
    }

    /// <summary>
    /// Extracts the simple class name from an assembly-qualified type name.
    /// </summary>
    /// <param name="eventType">The assembly-qualified type name (e.g., <c>"MyApp.Events.OrderCreated, MyApp, Version=1.0.0.0"</c>).</param>
    /// <returns>The simple class name (e.g., <c>"OrderCreated"</c>).</returns>
    private static string ExtractSimpleTypeName(string eventType)
    {
        var span = eventType.AsSpan();

        var commaIndex = span.IndexOf(',');
        if (commaIndex > 0)
        {
            span = span[..commaIndex];
        }

        var dotIndex = span.LastIndexOf('.');
        return (dotIndex >= 0 ? span[(dotIndex + 1)..] : span).ToString();
    }
}
