namespace NetEvolve.Pulse.Dispatchers;

using System.Collections.Concurrent;
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
/// All handlers are executed regardless of failures. If any handlers fail, an
/// <see cref="AggregateException"/> is thrown after all handlers have completed,
/// containing all exceptions that occurred.
/// <para><strong>Use Cases:</strong></para>
/// <list type="bullet">
/// <item><description>High-throughput event processing</description></item>
/// <item><description>Independent handlers with no ordering requirements</description></item>
/// <item><description>Handlers that don't share mutable state</description></item>
/// </list>
/// <para><strong>⚠️ Caution:</strong></para>
/// Not suitable when handler execution order matters or when handlers access shared mutable state.
/// Consider <see cref="SequentialEventDispatcher"/> for such scenarios.
/// Event handlers are resolved from the caller's scope, so scoped handlers share the caller's scoped
/// services. Do not combine this dispatcher with two or more handlers of the same event that use the
/// same scoped <c>DbContext</c> or database connection: they run concurrently on it, which EF Core and
/// most ADO.NET providers do not support. Register
/// <c>UseEventDispatcherFor&lt;TEvent, SequentialEventDispatcher&gt;()</c> for such events instead, or
/// <c>UseDefaultEventDispatcher&lt;SequentialEventDispatcher&gt;()</c> to make every event sequential.
/// The outbox handler registered by <c>AddOutbox()</c> is not affected: the mediator always runs it
/// first and on its own, and passes only the remaining handlers to this dispatcher.
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

        if (handlers is ICollection<IEventHandler<TEvent>> { Count: 1 } singleHandlerCollection)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var handler = singleHandlerCollection.First();

            try
            {
                await invoker(handler, message, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new AggregateException("One or more event handlers failed.", ex);
            }

            return;
        }

        var exceptions = new ConcurrentBag<Exception>();

        await Parallel
            .ForEachAsync(
                handlers,
                cancellationToken,
                async (handler, token) =>
                {
                    try
                    {
                        await invoker(handler, message, token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (!token.IsCancellationRequested)
                    {
                        exceptions.Add(ex);
                    }
                }
            )
            .ConfigureAwait(false);

        if (!exceptions.IsEmpty)
        {
            throw new AggregateException("One or more event handlers failed.", exceptions);
        }
    }
}
