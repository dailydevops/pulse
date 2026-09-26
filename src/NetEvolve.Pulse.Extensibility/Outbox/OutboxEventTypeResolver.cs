namespace NetEvolve.Pulse.Extensibility.Outbox;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;

/// <summary>
/// Resolves persisted outbox event type names back to <see cref="Type"/> instances and handles
/// event types that the reading application cannot resolve.
/// </summary>
/// <remarks>
/// <para><strong>Unresolvable Event Types:</strong></para>
/// An event type cannot be resolved when it was renamed or removed, or when it is not compiled into
/// a trimmed or NativeAOT application that reads the outbox. <see cref="Resolve"/> never fails for such
/// a name. It returns a placeholder <see cref="Type"/> whose <see cref="Type.AssemblyQualifiedName"/> is the
/// stored name, so that management and inspection reads can still list the message and persisting the
/// placeholder writes the stored name back unchanged.
/// <para><strong>Fetching:</strong></para>
/// Repositories call <see cref="DeadLetterUnresolvableAsync"/> on every fetched batch so that a message
/// with an unresolvable event type never blocks the other messages: it can never be delivered, so it is
/// moved to <see cref="OutboxMessageStatus.DeadLetter"/> with an error that names the stored type.
/// </remarks>
public static class OutboxEventTypeResolver
{
    /// <summary>
    /// Caches successfully resolved types by their persisted name, since <see cref="Type.GetType(string)"/>
    /// parses the name and probes loaded assemblies on every call in the polling hot path. The set of distinct
    /// event type names is small and bounded. Failed resolutions are not cached.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Type> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Resolves a persisted event type name to a <see cref="Type"/>.
    /// </summary>
    /// <param name="typeName">The event type name read from storage, usually assembly-qualified.</param>
    /// <returns>
    /// The resolved <see cref="Type"/>, or a placeholder whose <see cref="Type.AssemblyQualifiedName"/> is
    /// <paramref name="typeName"/> when the name cannot be resolved or is malformed.
    /// </returns>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2057:Unrecognized value passed to the parameter of method with 'DynamicallyAccessedMembersAttribute'",
        Justification = "The resolved event type is only used for its identity (grouping, naming, per-event-type options); no members are reflected on. A type that cannot be resolved in a trimmed or NativeAOT application is returned as a placeholder and dead-lettered on fetch."
    )]
    public static Type Resolve(string typeName)
    {
        ArgumentNullException.ThrowIfNull(typeName);

        if (_cache.TryGetValue(typeName, out var cached))
        {
            return cached;
        }

        Type? resolved;
        try
        {
            resolved = Type.GetType(typeName);
        }
        catch (Exception ex)
            when (ex is ArgumentException or IOException or BadImageFormatException or TypeLoadException)
        {
            // Type.GetType still throws for a malformed assembly name part (FileLoadException,
            // CultureNotFoundException) even though it returns null for a type that does not exist.
            resolved = null;
        }

        if (resolved is null)
        {
            return new UnresolvableEventType(typeName);
        }

        _cache[typeName] = resolved;
        return resolved;
    }

    /// <summary>
    /// Determines whether <paramref name="eventType"/> is the placeholder <see cref="Resolve"/> returns
    /// for an event type name that cannot be resolved.
    /// </summary>
    /// <param name="eventType">The event type of an outbox message.</param>
    /// <returns><see langword="true"/> if the event type could not be resolved; otherwise, <see langword="false"/>.</returns>
    internal static bool IsUnresolvable(Type eventType) => eventType is UnresolvableEventType;

    /// <summary>
    /// Moves every message of a fetched batch whose event type cannot be resolved to
    /// <see cref="OutboxMessageStatus.DeadLetter"/> and returns the remaining messages.
    /// </summary>
    /// <param name="repository">The repository that fetched and claimed <paramref name="messages"/>.</param>
    /// <param name="messages">The fetched batch.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// <paramref name="messages"/> itself when every event type was resolved; otherwise, a new list without
    /// the unresolvable messages, in the original order.
    /// </returns>
    /// <remarks>
    /// Call this after the claim has been committed. The unresolvable messages are dead-lettered with one
    /// <see cref="IOutboxRepository.MarkAsDeadLetterAsync(IReadOnlyCollection{Guid}, string, CancellationToken)"/>
    /// call per distinct stored name, with the error <c>Cannot resolve event type '&lt;stored name&gt;'.</c>
    /// Dead-lettering is best-effort: if it fails or is cancelled, the exception is swallowed so the resolvable
    /// messages of the claimed batch are still returned. The unresolvable messages are left in
    /// <see cref="OutboxMessageStatus.Processing"/> and are dead-lettered again once a provider that reclaims
    /// expired processing leases fetches them again.
    /// </remarks>
    public static async Task<IReadOnlyList<OutboxMessage>> DeadLetterUnresolvableAsync(
        this IOutboxRepository repository,
        IReadOnlyList<OutboxMessage> messages,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(messages);

        var unresolvable = messages.Where(m => m.EventType is UnresolvableEventType).ToList();
        if (unresolvable.Count == 0)
        {
            return messages;
        }

        foreach (var group in unresolvable.GroupBy(m => m.EventType.AssemblyQualifiedName, StringComparer.Ordinal))
        {
            try
            {
                await repository
                    .MarkAsDeadLetterAsync(
                        [.. group.Select(m => m.Id)],
                        $"Cannot resolve event type '{group.Key}'. The type was renamed or removed, or it is not compiled into the application that processes the outbox.",
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
#pragma warning disable RCS1075 // Best-effort: a failure must not lose the resolvable messages of the committed claim.
            catch (Exception)
#pragma warning restore RCS1075
            {
                // The messages stay in Processing; see remarks.
            }
        }

        return [.. messages.Where(m => m.EventType is not UnresolvableEventType)];
    }

    /// <summary>
    /// Placeholder for an event type that cannot be resolved. It reports the stored name as its
    /// <see cref="Type.AssemblyQualifiedName"/>, so <see cref="TypeExtensions.ToOutboxEventTypeName"/> and the
    /// persistence providers round-trip the stored name unchanged. Two placeholders are equal when they carry
    /// the same stored name.
    /// </summary>
    private sealed class UnresolvableEventType : TypeDelegator
    {
        private readonly string _typeName;
        private readonly string _fullName;

        public UnresolvableEventType(string typeName)
            : base(typeof(object))
        {
            _typeName = typeName;
            _fullName = GetFullName(typeName);
        }

        public override string AssemblyQualifiedName => _typeName;

        public override string FullName => _fullName;

        public override string Name => _fullName[(NamespaceLength + 1)..];

        public override string? Namespace => NamespaceLength > 0 ? _fullName[..NamespaceLength] : null;

        private int NamespaceLength =>
            _fullName.LastIndexOf(
                '.',
                _fullName.IndexOf('[', StringComparison.Ordinal) is var b and >= 0 ? b : _fullName.Length - 1
            );

        public override bool Equals(Type? o) => o is UnresolvableEventType other && other._typeName == _typeName;

        public override bool Equals(object? o) => o is UnresolvableEventType other && other._typeName == _typeName;

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_typeName);

        public override string ToString() => _fullName;

        /// <summary>
        /// Returns the type name without its assembly part: the text before the first comma outside of
        /// generic argument brackets.
        /// </summary>
        private static string GetFullName(string typeName)
        {
            var depth = 0;
            for (var i = 0; i < typeName.Length; i++)
            {
                switch (typeName[i])
                {
                    case '[':
                        depth++;
                        break;
                    case ']':
                        depth--;
                        break;
                    case ',' when depth == 0:
                        return typeName[..i].Trim();
                }
            }

            return typeName.Trim();
        }
    }
}
