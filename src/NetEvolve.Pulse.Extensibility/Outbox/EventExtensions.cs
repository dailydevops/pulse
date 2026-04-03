namespace NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Extension methods for <see cref="IEvent"/> in the context of the outbox pattern.
/// </summary>
public static class EventExtensions
{
    /// <summary>
    /// Parses the event's <see cref="IEvent.Id"/> as a <see cref="Guid"/>, or generates a new one if the value is absent or not a valid GUID.
    /// </summary>
    /// <param name="event">The event whose identifier to resolve.</param>
    /// <returns>The parsed <see cref="Guid"/> from <see cref="IEvent.Id"/>, or a newly generated <see cref="Guid"/>.</returns>
    public static Guid ToOutboxId(this IEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        return Guid.TryParse(@event.Id, out var parsedId) ? parsedId :
#if NET9_0_OR_GREATER
            Guid.CreateVersion7();
#else
            Guid.NewGuid();
#endif
    }
}
