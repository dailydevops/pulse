namespace NetEvolve.Pulse.Dispatchers;

using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Event dispatcher that limits concurrent handler execution using a semaphore.
/// Protects downstream systems from being overwhelmed by controlling parallelism.
/// </summary>
/// <remarks>
/// <para><strong>Execution Behavior:</strong></para>
/// Limits the number of handlers executing simultaneously to the configured maximum.
/// Handlers waiting for a semaphore slot respect the cancellation token.
/// <para><strong>Thread Safety:</strong></para>
/// This implementation is thread-safe and uses <see cref="SemaphoreSlim"/> for efficient async coordination.
/// <para><strong>Error Handling:</strong></para>
/// Individual handler failures do not prevent other handlers from executing.
/// The semaphore is always released in a finally block to prevent deadlocks.
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
public sealed class RateLimitedEventDispatcher : IEventDispatcher, IDisposable
{
    /// <summary>
    /// Semaphore controlling the maximum number of concurrent handler executions.
    /// </summary>
    private readonly SemaphoreSlim _semaphore;

    /// <summary>
    /// Flag indicating whether the dispatcher has been disposed.
    /// </summary>
    private bool _disposed;

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
        _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Each handler acquisition waits for a semaphore slot before execution.
    /// If the cancellation token is triggered while waiting, the handler is skipped.
    /// The semaphore is released in a finally block to ensure proper cleanup even on exceptions.
    /// </remarks>
    public async Task DispatchAsync<TEvent>(
        TEvent message,
        IEnumerable<IEventHandler<TEvent>> handlers,
        Func<IEventHandler<TEvent>, TEvent, Task> invoker,
        CancellationToken cancellationToken
    )
        where TEvent : IEvent
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var tasks = handlers.Select(async handler =>
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await invoker(handler, message).ConfigureAwait(false);
            }
            finally
            {
                _ = _semaphore.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Releases the semaphore resources used by this dispatcher.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _semaphore.Dispose();
        _disposed = true;
    }
}
