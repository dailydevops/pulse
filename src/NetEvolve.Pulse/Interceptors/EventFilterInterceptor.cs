namespace NetEvolve.Pulse.Interceptors;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Event interceptor that evaluates all registered <see cref="IEventFilter{TEvent}"/> implementations
/// before forwarding the event to its handlers.
/// </summary>
/// <typeparam name="TEvent">The type of event to intercept, which must implement <see cref="IEvent"/>.</typeparam>
/// <remarks>
/// <para><strong>Behavior:</strong></para>
/// <list type="number">
/// <item><description>If no filters are registered, the event is forwarded to the handler unchanged (zero-overhead pass-through).</description></item>
/// <item><description>Each registered filter is evaluated in order. If all filters return <see langword="true"/>, the handler is invoked.</description></item>
/// <item><description>If any filter returns <see langword="false"/>, the handler is skipped silently (no exception is thrown).</description></item>
/// </list>
/// <para><strong>Registration:</strong></para>
/// Use <c>AddEventFilter()</c> on the <see cref="IMediatorBuilder"/> to register filters and this interceptor.
/// </remarks>
/// <seealso cref="IEventFilter{TEvent}"/>
internal sealed class EventFilterInterceptor<TEvent> : IEventInterceptor<TEvent>
    where TEvent : IEvent
{
    private readonly IEnumerable<IEventFilter<TEvent>> _filters;

    /// <summary>
    /// Initializes a new instance of the <see cref="EventFilterInterceptor{TEvent}"/> class.
    /// </summary>
    /// <param name="filters">The collection of filters to evaluate.</param>
    public EventFilterInterceptor(IEnumerable<IEventFilter<TEvent>> filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        _filters = filters;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        TEvent message,
        Func<TEvent, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(handler);

        foreach (var filter in _filters)
        {
            if (!await filter.ShouldHandleAsync(message, cancellationToken).ConfigureAwait(false))
            {
                return;
            }
        }

        await handler(message, cancellationToken).ConfigureAwait(false);
    }
}
