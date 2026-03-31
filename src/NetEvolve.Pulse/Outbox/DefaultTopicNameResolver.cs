namespace NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Default implementation of <see cref="ITopicNameResolver"/> that returns the simple class name
/// of the event type.
/// </summary>
/// <remarks>
/// For example, an event of type <c>MyApp.Events.OrderCreated</c> resolves to <c>"OrderCreated"</c>.
/// </remarks>
internal sealed class DefaultTopicNameResolver : ITopicNameResolver
{
    /// <inheritdoc />
    public string Resolve(OutboxMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message.EventType.Name;
    }
}
