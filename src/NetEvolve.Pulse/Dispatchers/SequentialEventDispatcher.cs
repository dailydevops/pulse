namespace NetEvolve.Pulse.Dispatchers;

using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Event dispatcher that executes handlers sequentially in registration order.
/// Suitable when handler execution order matters or handlers access shared state.
/// </summary>
/// <remarks>
/// <para><strong>Execution Behavior:</strong></para>
/// Handlers execute one at a time in the order they were registered in the DI container.
/// Each handler completes before the next one starts.
/// <para><strong>Error Handling:</strong></para>
/// Individual handler failures do not prevent subsequent handlers from executing.
/// Errors are handled by the invoker delegate which logs exceptions appropriately.
/// <para><strong>Use Cases:</strong></para>
/// <list type="bullet">
/// <item><description>Handlers with ordering dependencies</description></item>
/// <item><description>Handlers accessing shared mutable state</description></item>
/// <item><description>Debugging and troubleshooting event flows</description></item>
/// <item><description>Rate-sensitive downstream systems</description></item>
/// </list>
/// <para><strong>⚠️ Performance Consideration:</strong></para>
/// Sequential execution may significantly impact throughput for high-volume event processing.
/// Consider <see cref="ParallelEventDispatcher"/> when order independence is guaranteed.
/// </remarks>
/// <example>
/// <code>
/// // Register sequential dispatcher
/// services.AddPulse(config =>
/// {
///     config.UseDefaultEventDispatcher&lt;SequentialEventDispatcher&gt;();
/// });
/// </code>
/// </example>
/// <seealso cref="IEventDispatcher"/>
/// <seealso cref="ParallelEventDispatcher"/>
public sealed class SequentialEventDispatcher : IEventDispatcher
{
    /// <inheritdoc />
    /// <remarks>
    /// Iterates through handlers sequentially, awaiting each handler before proceeding to the next.
    /// Respects cancellation between handler invocations.
    /// </remarks>
    public async Task DispatchAsync<TEvent>(
        TEvent message,
        IEnumerable<IEventHandler<TEvent>> handlers,
        Func<IEventHandler<TEvent>, TEvent, Task> invoker,
        CancellationToken cancellationToken
    )
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(invoker);

        foreach (var handler in handlers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await invoker(handler, message).ConfigureAwait(false);
        }
    }
}
