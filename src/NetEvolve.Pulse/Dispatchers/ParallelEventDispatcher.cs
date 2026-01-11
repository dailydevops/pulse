namespace NetEvolve.Pulse.Dispatchers;

using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Event dispatcher that executes handlers concurrently using parallel iteration.
/// This is the default dispatcher providing optimal throughput for independent event handlers.
/// </summary>
/// <remarks>
/// <para><strong>Execution Behavior:</strong></para>
/// Handlers execute in parallel, leveraging available CPU cores for maximum throughput.
/// The degree of parallelism is managed by the runtime based on system resources.
/// <para><strong>Error Handling:</strong></para>
/// Individual handler failures do not prevent other handlers from executing.
/// Errors are handled by the invoker delegate which logs exceptions appropriately.
/// <para><strong>Use Cases:</strong></para>
/// <list type="bullet">
/// <item><description>High-throughput event processing</description></item>
/// <item><description>Independent handlers with no ordering requirements</description></item>
/// <item><description>Handlers that don't share mutable state</description></item>
/// </list>
/// <para><strong>⚠️ Caution:</strong></para>
/// Not suitable when handler execution order matters or when handlers access shared mutable state.
/// Consider <see cref="SequentialEventDispatcher"/> for such scenarios.
/// </remarks>
/// <example>
/// <code>
/// // Explicitly register parallel dispatcher (default behavior)
/// services.AddPulse(config =>
/// {
///     config.UseDefaultEventDispatcher&lt;ParallelEventDispatcher&gt;();
/// });
/// </code>
/// </example>
/// <seealso cref="IEventDispatcher"/>
/// <seealso cref="SequentialEventDispatcher"/>
public sealed class ParallelEventDispatcher : IEventDispatcher
{
    /// <inheritdoc />
    /// <remarks>
    /// Uses <see cref="Parallel.ForEachAsync{TSource}(IEnumerable{TSource}, CancellationToken, Func{TSource, CancellationToken, ValueTask})"/>
    /// for efficient parallel execution with automatic partitioning and work stealing.
    /// </remarks>
    public Task DispatchAsync<TEvent>(
        TEvent message,
        IEnumerable<IEventHandler<TEvent>> handlers,
        Func<IEventHandler<TEvent>, TEvent, Task> invoker,
        CancellationToken cancellationToken
    )
        where TEvent : IEvent =>
        Parallel.ForEachAsync(
            handlers,
            cancellationToken,
            async (handler, ct) => await invoker(handler, message).ConfigureAwait(false)
        );
}
