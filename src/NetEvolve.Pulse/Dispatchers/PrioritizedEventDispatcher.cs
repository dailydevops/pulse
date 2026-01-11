namespace NetEvolve.Pulse.Dispatchers;

using NetEvolve.Pulse;
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
/// <item><description>Handlers with equal priority maintain their registration order (stable sort)</description></item>
/// </list>
/// <para><strong>Execution Behavior:</strong></para>
/// Handlers execute sequentially in priority order. Each handler completes before the next starts.
/// This ensures predictable ordering for handlers with dependencies.
/// <para><strong>Error Handling:</strong></para>
/// Individual handler failures do not prevent subsequent handlers from executing.
/// Errors are handled by the invoker delegate which logs exceptions appropriately.
/// <para><strong>Use Cases:</strong></para>
/// <list type="bullet">
/// <item><description>Validation handlers that must run before business logic</description></item>
/// <item><description>Security checks that must execute first</description></item>
/// <item><description>Audit handlers that must capture final state</description></item>
/// <item><description>Notification handlers with delivery priority</description></item>
/// </list>
/// <para><strong>⚠️ Performance Consideration:</strong></para>
/// Sequential execution impacts throughput. Use only when handler ordering is critical.
/// Consider <see cref="ParallelEventDispatcher"/> for independent handlers.
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
    /// Sorts handlers by priority before execution. Uses a stable sort algorithm to preserve
    /// registration order for handlers with equal priority. Non-prioritized handlers are
    /// assigned <see cref="int.MaxValue"/> and execute after all prioritized handlers.
    /// </remarks>
    public async Task DispatchAsync<TEvent>(
        TEvent message,
        IEnumerable<IEventHandler<TEvent>> handlers,
        Func<IEventHandler<TEvent>, TEvent, Task> invoker,
        CancellationToken cancellationToken
    )
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(invoker);

        // Sort handlers by priority, preserving order for equal priorities
        var orderedHandlers = handlers
            .Select((handler, index) => (Handler: handler, Priority: GetPriority(handler), Index: index))
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.Index)
            .Select(x => x.Handler);

        foreach (var handler in orderedHandlers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await invoker(handler, message).ConfigureAwait(false);
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
