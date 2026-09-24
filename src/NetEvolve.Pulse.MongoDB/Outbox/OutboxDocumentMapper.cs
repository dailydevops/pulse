namespace NetEvolve.Pulse.Outbox;

using System;
using System.Collections.Concurrent;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Provides conversion methods between <see cref="OutboxDocument"/> and <see cref="OutboxMessage"/>.
/// </summary>
internal static class OutboxDocumentMapper
{
    /// <summary>
    /// Caches resolved event types by their persisted type name, since <see cref="Type.GetType(string)"/>
    /// parses the assembly-qualified name and probes loaded assemblies on every call. Only successful
    /// resolutions are cached; unresolvable names keep failing on each encounter.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Type> EventTypeCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Converts an <see cref="OutboxDocument"/> retrieved from MongoDB to an <see cref="OutboxMessage"/>.
    /// </summary>
    /// <param name="doc">The document to convert.</param>
    /// <returns>A new <see cref="OutboxMessage"/> populated from <paramref name="doc"/>.</returns>
    internal static OutboxMessage ToOutboxMessage(OutboxDocument doc) =>
        new OutboxMessage
        {
            Id = doc.Id,
            EventType = ResolveEventType(doc.EventType),
            Payload = doc.Payload,
            CorrelationId = doc.CorrelationId,
            CausationId = doc.CausationId,
            CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(doc.CreatedAt, DateTimeKind.Utc), TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(doc.UpdatedAt, DateTimeKind.Utc), TimeSpan.Zero),
            ProcessedAt = doc.ProcessedAt.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(doc.ProcessedAt.Value, DateTimeKind.Utc), TimeSpan.Zero)
                : (DateTimeOffset?)null,
            NextRetryAt = doc.NextRetryAt.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(doc.NextRetryAt.Value, DateTimeKind.Utc), TimeSpan.Zero)
                : (DateTimeOffset?)null,
            RetryCount = doc.RetryCount,
            Error = doc.Error,
            Status = (OutboxMessageStatus)doc.Status,
        };

    /// <summary>
    /// Converts an <see cref="OutboxMessage"/> to an <see cref="OutboxDocument"/> for storage.
    /// </summary>
    /// <param name="message">The message to convert.</param>
    /// <returns>A new <see cref="OutboxDocument"/> populated from <paramref name="message"/>.</returns>
    internal static OutboxDocument ToDocument(OutboxMessage message) =>
        new OutboxDocument
        {
            Id = message.Id,
            EventType = message.EventType.ToOutboxEventTypeName(),
            Payload = message.Payload,
            CorrelationId = message.CorrelationId,
            CausationId = message.CausationId,
            CreatedAt = message.CreatedAt.UtcDateTime,
            UpdatedAt = message.UpdatedAt.UtcDateTime,
            ProcessedAt = message.ProcessedAt.HasValue ? message.ProcessedAt.Value.UtcDateTime : (DateTime?)null,
            NextRetryAt = message.NextRetryAt.HasValue ? message.NextRetryAt.Value.UtcDateTime : (DateTime?)null,
            RetryCount = message.RetryCount,
            Error = message.Error,
            Status = (int)message.Status,
        };

    /// <summary>
    /// Resolves the event <see cref="Type"/> for the given persisted type name, using a cache to avoid
    /// repeated reflection lookups for the same name.
    /// </summary>
    /// <param name="eventTypeName">The persisted, assembly-qualified event type name.</param>
    /// <returns>The resolved event <see cref="Type"/>.</returns>
    /// <exception cref="InvalidOperationException">The type name cannot be resolved.</exception>
    private static Type ResolveEventType(string eventTypeName) =>
        EventTypeCache.TryGetValue(eventTypeName, out var eventType)
            ? eventType
            : EventTypeCache.GetOrAdd(
                eventTypeName,
                static name =>
                    Type.GetType(name) ?? throw new InvalidOperationException($"Cannot resolve event type '{name}'.")
            );
}
