namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Represents an event that can be published through the mediator to notify multiple handlers.
/// Events are used for asynchronous, decoupled communication where multiple subscribers may react to the same occurrence.
/// Unlike commands and queries, events can have zero or more handlers.
/// </summary>
public interface IEvent
{
    /// <summary>
    /// Gets the unique identifier for this event instance.
    /// This identifier can be used for correlation, logging, and tracking purposes.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets or sets the timestamp when this event was published through the mediator.
    /// This property is automatically set by the mediator when <see cref="IMediator.PublishAsync{TEvent}"/> is called.
    /// </summary>
    DateTimeOffset? PublishedAt { get; internal set; }
}
