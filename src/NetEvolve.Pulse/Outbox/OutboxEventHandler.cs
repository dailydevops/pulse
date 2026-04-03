namespace NetEvolve.Pulse.Outbox;

using System.Threading.Tasks;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// An <see cref="IEventHandler{TEvent}"/> that forwards eligible events to the outbox for reliable,
/// asynchronous processing. Events implementing <see cref="IEventInProcess"/> are skipped.
/// </summary>
/// <typeparam name="TEvent">The type of event to handle.</typeparam>
/// <remarks>
/// This handler is registered as an open-generic <see cref="IEventHandler{TEvent}"/> and captures all
/// published events. It persists each event to the outbox via <see cref="IEventOutbox.StoreAsync{TEvent}"/>
/// unless the event is an <see cref="IEventInProcess"/>, which is silently bypassed.
/// </remarks>
internal sealed class OutboxEventHandler<TEvent> : IEventHandler<TEvent>
    where TEvent : IEvent
{
    /// <summary>The outbox used to persist events for reliable delivery.</summary>
    private readonly IEventOutbox _eventOutbox;

    /// <summary>
    /// Initializes a new instance of <see cref="OutboxEventHandler{TEvent}"/>.
    /// </summary>
    /// <param name="eventOutbox">The outbox store to which eligible events are persisted.</param>
    public OutboxEventHandler(IEventOutbox eventOutbox) => _eventOutbox = eventOutbox;

    /// <summary>
    /// Stores <paramref name="message"/> in the outbox, unless it implements <see cref="IEventInProcess"/>
    /// and <see cref="IEventInProcess.HandleInProcess"/> returns <see langword="true"/>,
    /// in which case the event is silently skipped.
    /// </summary>
    /// <param name="message">The event to process.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous store operation, or a completed task when skipped.</returns>
    public async Task HandleAsync(TEvent message, CancellationToken cancellationToken = default)
    {
        if (message is IEventInProcess { HandleInProcess: true })
        {
            // Skip outbox processing for in-process events.
            return;
        }

        await _eventOutbox.StoreAsync(message, cancellationToken).ConfigureAwait(false);
    }
}
