namespace NetEvolve.Pulse.Dispatchers;

using System.Collections.Concurrent;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Event dispatcher that limits concurrent handler execution to a configurable maximum.
/// Protects downstream systems from being overwhelmed by controlling parallelism.
/// </summary>
/// <remarks>
/// <para><strong>Execution Behavior:</strong></para>
/// Uses <see cref="Parallel.ForEachAsync{TSource}(IEnumerable{TSource}, ParallelOptions, Func{TSource, CancellationToken, ValueTask})"/>
/// with <see cref="ParallelOptions.MaxDegreeOfParallelism"/> set to <see cref="MaxConcurrency"/> to limit
/// the number of handlers executing simultaneously. Cancellation is respected between handler invocations.
/// <para><strong>Thread Safety:</strong></para>
/// This implementation is thread-safe. Concurrency is controlled by <see cref="Parallel"/> via
/// <see cref="ParallelOptions.MaxDegreeOfParallelism"/>; no external synchronisation is required.
/// <para><strong>Error Handling:</strong></para>
/// Individual handler failures do not prevent other handlers from executing.
/// All handlers are executed regardless of failures. If any handlers fail, an
/// <see cref="AggregateException"/> is thrown after all handlers have completed,
/// containing all exceptions that occurred.
/// <para><strong>Use Cases:</strong></para>
/// <list type="bullet">
/// <item><description>Protecting rate-limited external APIs</description></item>
/// <item><description>Controlling database connection usage</description></item>
/// <item><description>Preventing memory exhaustion from too many concurrent operations</description></item>
/// <item><description>Throttling notifications to messaging systems</description></item>
/// </list>
/// <para><strong>⚠️ Configuration Guidance:</strong></para>
/// <list type="bullet">
/// <item><description>Set <see cref="MaxConcurrency"/> based on downstream system capacity</description></item>
/// <item><description>Consider using scoped lifetime when concurrency varies per request</description></item>
/// <item><description>Monitor queue depth in high-throughput scenarios</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // Register rate-limited dispatcher with default concurrency (5)
/// services.AddPulse(config =&gt;
/// {
///     config.UseDefaultEventDispatcher&lt;RateLimitedEventDispatcher&gt;();
/// });
///
/// // Register with custom max concurrency via constructor
/// services.AddSingleton(new RateLimitedEventDispatcher(maxConcurrency: 10));
/// services.AddPulse(config =&gt;
/// {
///     config.UseDefaultEventDispatcher&lt;RateLimitedEventDispatcher&gt;();
/// });
/// </code>
/// </example>
/// <seealso cref="IEventDispatcher"/>
/// <seealso cref="ParallelEventDispatcher"/>
/// <seealso cref="SequentialEventDispatcher"/>
public sealed class RateLimitedEventDispatcher : IEventDispatcher
{
    /// <summary>
    /// Gets the maximum number of handlers that can execute concurrently.
    /// </summary>
    /// <value>The configured maximum concurrency level.</value>
    public int MaxConcurrency { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimitedEventDispatcher"/> class with a default concurrency of 5.
    /// </summary>
    public RateLimitedEventDispatcher()
        : this(maxConcurrency: 5) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimitedEventDispatcher"/> class with the specified concurrency limit.
    /// </summary>
    /// <param name="maxConcurrency">The maximum number of handlers that can execute concurrently. Must be at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="maxConcurrency"/> is less than 1.</exception>
    public RateLimitedEventDispatcher(int maxConcurrency)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);

        MaxConcurrency = maxConcurrency;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uses <see cref="Parallel.ForEachAsync{TSource}(IEnumerable{TSource}, ParallelOptions, Func{TSource, CancellationToken, ValueTask})"/>
    /// with <see cref="ParallelOptions.MaxDegreeOfParallelism"/> capped at <see cref="MaxConcurrency"/>.
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
        var exceptions = new ConcurrentBag<Exception>();
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = MaxConcurrency,
            CancellationToken = cancellationToken,
        };

        await Parallel
            .ForEachAsync(
                handlers,
                options,
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
