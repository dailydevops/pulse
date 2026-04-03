namespace NetEvolve.Pulse.Dispatchers;

using System.Collections.Concurrent;
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
/// <item><description>Handlers with equal priority execute in parallel within the same priority group</description></item>
/// </list>
/// <para><strong>Execution Behavior:</strong></para>
/// Priority groups execute sequentially in ascending priority order. Within each group, handlers
/// execute in parallel using <see cref="System.Threading.Tasks.Parallel"/>.
/// This ensures predictable ordering between groups while maximising throughput within each group.
/// <para><strong>Error Handling:</strong></para>
/// Individual handler failures do not prevent other handlers from executing, including handlers
/// in subsequent priority groups. All handlers across all groups are executed regardless of
/// failures. If any handlers fail, an <see cref="AggregateException"/> is thrown after all
/// handlers have completed, containing all exceptions that occurred.
/// <para><strong>Use Cases:</strong></para>
/// <list type="bullet">
/// <item><description>Validation handlers that must run before business logic</description></item>
/// <item><description>Security checks that must execute first</description></item>
/// <item><description>Audit handlers that must capture final state</description></item>
/// <item><description>Notification handlers with delivery priority</description></item>
/// </list>
/// <para><strong>⚠️ Performance Consideration:</strong></para>
/// Sequential group execution impacts overall throughput compared to fully parallel execution.
/// Use only when handler ordering across groups is critical.
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
    /// Groups handlers by priority and executes each group sequentially in ascending order.
    /// Within each group, handlers execute in parallel using
    /// <see cref="Parallel.ForEachAsync{TSource}(IEnumerable{TSource}, CancellationToken, Func{TSource, CancellationToken, ValueTask})"/>.
    /// Non-prioritized handlers are assigned <see cref="int.MaxValue"/> and execute in the last group.
    /// Exceptions from individual handlers are collected and thrown as an <see cref="AggregateException"/>
    /// after all handlers across all groups have completed.
    /// </remarks>
    public async Task DispatchAsync<TEvent>(
        TEvent message,
        IEnumerable<IEventHandler<TEvent>> handlers,
        Func<IEventHandler<TEvent>, TEvent, CancellationToken, Task> invoker,
        CancellationToken cancellationToken
    )
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(invoker);

        // Sort handlers by priority, preserving order for equal priorities
        var priorityGroups = handlers
            .Select(handler => (Handler: handler, Priority: GetPriority(handler)))
            .GroupBy(x => x.Priority)
            .OrderBy(x => x.Key);

        var exceptions = new ConcurrentBag<Exception>();

        foreach (var group in priorityGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await Parallel
                .ForEachAsync(
                    group.Select(x => x.Handler),
                    cancellationToken,
                    async (handler, ct) =>
                    {
                        try
                        {
                            await invoker(handler, message, ct).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            exceptions.Add(ex);
                        }
                    }
                )
                .ConfigureAwait(false);
        }

        if (!exceptions.IsEmpty)
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
