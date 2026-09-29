namespace NetEvolve.Pulse.Extensibility;

using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Defines the contract for dispatching events to their registered handlers.
/// Implementations determine the execution strategy (parallel, sequential, rate-limited, prioritized).
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong></para>
/// The event dispatcher abstraction decouples the event dispatch strategy from the mediator implementation,
/// enabling pluggable execution patterns for different application requirements.
/// <para><strong>Built-in Implementations:</strong></para>
/// <list type="bullet">
/// <item><description><c>ParallelEventDispatcher</c>: Executes handlers concurrently for maximum throughput (default)</description></item>
/// <item><description><c>SequentialEventDispatcher</c>: Executes handlers one at a time in registration order</description></item>
/// <item><description><c>RateLimitedEventDispatcher</c>: Limits concurrent execution to protect downstream systems</description></item>
/// <item><description><c>PrioritizedEventDispatcher</c>: Orders handlers by <see cref="IPrioritizedEventHandler{TEvent}.Priority"/> before execution</description></item>
/// </list>
/// The outbox handler registered by <c>AddOutbox()</c> is never passed to a dispatcher: the mediator always runs it
/// first and on its own, and passes only the remaining handlers to the dispatcher.
/// For reliable delivery through <see cref="IEventOutbox"/>, register the outbox with <c>AddOutbox()</c>; it is not a dispatcher.
/// <para><strong>Custom Implementations:</strong></para>
/// Implement this interface for advanced scenarios such as:
/// <list type="bullet">
/// <item><description>Custom rate-limiting with token bucket algorithms</description></item>
/// <item><description>Circuit breaker patterns for fault tolerance</description></item>
/// <item><description>Batching for bulk operations</description></item>
/// </list>
/// <para><strong>Error Handling:</strong></para>
/// Implementations SHOULD continue executing remaining handlers when individual handlers fail,
/// logging errors appropriately. The parallel dispatcher aggregates all exceptions.
/// </remarks>
/// <example>
/// <code>
/// // Register built-in dispatchers
/// services.AddPulse(config =>
/// {
///     // Sequential execution
///     config.UseDefaultEventDispatcher&lt;SequentialEventDispatcher&gt;();
///
///     // Or rate-limited execution
///     config.UseDefaultEventDispatcher&lt;RateLimitedEventDispatcher&gt;();
///
///     // Or prioritized execution
///     config.UseDefaultEventDispatcher&lt;PrioritizedEventDispatcher&gt;();
/// });
///
/// // Event-specific dispatcher
/// services.AddPulse(config =>
/// {
///     config.UseEventDispatcherFor&lt;CriticalEvent, SequentialEventDispatcher&gt;();
/// });
/// </code>
/// </example>
/// <seealso cref="IEvent"/>
/// <seealso cref="IEventHandler{TEvent}"/>
/// <seealso cref="IPrioritizedEventHandler{TEvent}"/>
/// <seealso cref="IEventOutbox"/>
/// <seealso cref="IMediatorBuilder"/>
public interface IEventDispatcher
{
    /// <summary>
    /// Dispatches an event to all registered handlers using the implementation's execution strategy.
    /// </summary>
    /// <typeparam name="TEvent">The type of event to dispatch.</typeparam>
    /// <param name="message">The event message to dispatch to handlers.</param>
    /// <param name="handlers">The collection of handlers to receive the event.</param>
    /// <param name="invoker">
    /// A delegate that invokes a single handler with the event.
    /// It calls the handler and logs a failure before rethrowing it; it applies no interceptors and creates no activity.
    /// The delegate receives the handler, the event message, and a cancellation token.
    /// </param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous dispatch operation.</returns>
    /// <remarks>
    /// <para><strong>Handler Invocation:</strong></para>
    /// Use the <paramref name="invoker"/> delegate to execute each handler so that failures are logged consistently.
    /// <see cref="IEventInterceptor{TEvent}"/> instances do not run per handler: the mediator wraps the whole
    /// <see cref="DispatchAsync{TEvent}"/> call once with the interceptor pipeline.
    /// <para><strong>Implementation Guidelines:</strong></para>
    /// <list type="bullet">
    /// <item><description>MUST invoke all handlers unless cancelled</description></item>
    /// <item><description>SHOULD handle individual handler failures gracefully</description></item>
    /// <item><description>SHOULD respect cancellation token in loops</description></item>
    /// <item><description>MAY implement custom ordering, throttling, or batching logic</description></item>
    /// </list>
    /// </remarks>
    Task DispatchAsync<TEvent>(
        TEvent message,
        IEnumerable<IEventHandler<TEvent>> handlers,
        Func<IEventHandler<TEvent>, TEvent, CancellationToken, Task> invoker,
        CancellationToken cancellationToken
    )
        where TEvent : IEvent;
}
