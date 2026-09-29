namespace NetEvolve.Pulse.Dispatchers;

using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Event dispatcher that executes handlers in priority order based on <see cref="IPrioritizedEventHandler{TEvent}"/>.
/// Handlers with lower priority values execute first, enabling controlled execution ordering.
/// </summary>
/// <remarks>
/// <para><strong>Priority Resolution:</strong></para>
/// <list type="bullet">
/// <item><description>Handlers implementing <see cref="IPrioritizedEventHandler{TEvent}"/> are sorted by <see cref="IPrioritizedEventHandler{TEvent}.Priority"/></description></item>
/// <item><description>Handlers not implementing the interface are treated as priority <see cref="int.MaxValue"/> (execute last)</description></item>
/// <item><description>Handlers with equal priority execute in registration order</description></item>
/// </list>
/// <para><strong>Execution Behavior:</strong></para>
/// Handlers execute sequentially, one at a time, in ascending priority order. Each handler is awaited
/// before the next one starts, so the execution order is fully deterministic.
/// <para><strong>Error Handling:</strong></para>
/// Individual handler failures do not prevent other handlers from executing, including handlers
/// with a higher priority value. If any handlers fail, an <see cref="AggregateException"/> is thrown
/// after all handlers have completed, containing all exceptions that occurred.
/// Cancellation of the caller's token stops dispatching and surfaces as <see cref="OperationCanceledException"/>.
/// <para><strong>Use Cases:</strong></para>
/// <list type="bullet">
/// <item><description>Validation handlers that must run before business logic</description></item>
/// <item><description>Security checks that must execute first</description></item>
/// <item><description>Audit handlers that must capture final state</description></item>
/// <item><description>Notification handlers with delivery priority</description></item>
/// </list>
/// <para><strong>⚠️ Performance Consideration:</strong></para>
/// Sequential execution impacts overall throughput compared to parallel execution.
/// Use only when handler ordering is critical.
/// Consider <see cref="ParallelEventDispatcher"/> for independent handlers.
/// <para><strong>Outbox:</strong></para>
/// The outbox handler registered by <c>AddOutbox()</c> is never passed to this dispatcher: the mediator
/// always runs it first and on its own, and passes only the remaining handlers here.
/// </remarks>
/// <example>
/// <code>
/// // Register prioritized dispatcher
/// services.AddPulse(config =&gt;
/// {
///     config.UseDefaultEventDispatcher&lt;PrioritizedEventDispatcher&gt;();
/// });
///
/// // Implement prioritized handler
/// public class ValidationHandler : IPrioritizedEventHandler&lt;OrderEvent&gt;
/// {
///     public int Priority =&gt; 0; // Runs first
///     public Task HandleAsync(OrderEvent msg, CancellationToken ct) =&gt; ...;
/// }
/// </code>
/// </example>
/// <seealso cref="IEventDispatcher"/>
/// <seealso cref="IPrioritizedEventHandler{TEvent}"/>
/// <seealso cref="SequentialEventDispatcher"/>
public sealed class PrioritizedEventDispatcher : IEventDispatcher
{
    /// <inheritdoc />
    /// <remarks>
    /// Sorts handlers by priority with a stable sort, so equal priorities keep their registration order,
    /// and awaits each handler before invoking the next one.
    /// Non-prioritized handlers are assigned <see cref="int.MaxValue"/> and execute last.
    /// Respects cancellation between handler invocations.
    /// Exceptions from individual handlers are collected and thrown as an <see cref="AggregateException"/>
    /// after all handlers have completed.
    /// </remarks>
    public async Task DispatchAsync<TEvent>(
        TEvent message,
        IEnumerable<IEventHandler<TEvent>> handlers,
        Func<IEventHandler<TEvent>, TEvent, CancellationToken, Task> invoker,
        CancellationToken cancellationToken
    )
        where TEvent : IEvent
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(invoker);

        var exceptions = new List<Exception>();

        // OrderBy is a stable sort: handlers with equal priority keep their registration order.
        foreach (var handler in handlers.OrderBy(GetPriority))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await invoker(handler, message, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                exceptions.Add(ex);
            }
        }

        if (exceptions is { Count: > 0 })
        {
            throw new AggregateException("One or more event handlers failed.", exceptions);
        }
    }

    /// <summary>
    /// Gets the priority value for a handler.
    /// </summary>
    /// <typeparam name="TEvent">The type of event being handled.</typeparam>
    /// <param name="handler">The handler to get priority for.</param>
    /// <returns>
    /// The handler's <see cref="IPrioritizedEventHandler{TEvent}.Priority"/> if implemented;
    /// otherwise <see cref="int.MaxValue"/>.
    /// </returns>
    private static int GetPriority<TEvent>(IEventHandler<TEvent> handler)
        where TEvent : IEvent =>
        handler is IPrioritizedEventHandler<TEvent> prioritized ? prioritized.Priority : int.MaxValue;
}
