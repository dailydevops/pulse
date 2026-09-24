namespace NetEvolve.Pulse.Interceptors;

using System;
using System.Threading;
using System.Threading.Tasks;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// An <see cref="IEventFilter{TEvent}"/> implementation that wraps a predicate delegate.
/// </summary>
/// <typeparam name="TEvent">The type of event to filter.</typeparam>
internal sealed class PredicateEventFilter<TEvent> : IEventFilter<TEvent>
    where TEvent : IEvent
{
    private readonly Func<TEvent, CancellationToken, ValueTask<bool>> _predicate;

    /// <summary>
    /// Initializes a new instance of the <see cref="PredicateEventFilter{TEvent}"/> class.
    /// </summary>
    /// <param name="predicate">The predicate function used to determine whether the event should be handled.</param>
    public PredicateEventFilter(Func<TEvent, CancellationToken, ValueTask<bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _predicate = predicate;
    }

    /// <inheritdoc />
    public ValueTask<bool> ShouldHandleAsync(TEvent message, CancellationToken cancellationToken = default) =>
        _predicate(message, cancellationToken);
}
