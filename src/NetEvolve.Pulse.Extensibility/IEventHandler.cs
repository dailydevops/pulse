namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Defines a handler for processing events of type <typeparamref name="TEvent"/>.
/// Multiple handlers can be registered for the same event type, and all will be executed in parallel when the event is published.
/// Event handlers should be idempotent and handle failures gracefully as exceptions in one handler won't affect others.
/// </summary>
/// <typeparam name="TEvent">The type of event to handle, which must implement <see cref="IEvent"/>.</typeparam>
public interface IEventHandler<in TEvent>
    where TEvent : IEvent
{
    /// <summary>
    /// Asynchronously handles the specified event.
    /// This method is invoked when an event of type <typeparamref name="TEvent"/> is published through the mediator.
    /// </summary>
    /// <param name="message">The event to handle.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task HandleAsync(TEvent message, CancellationToken cancellationToken = default);
}
