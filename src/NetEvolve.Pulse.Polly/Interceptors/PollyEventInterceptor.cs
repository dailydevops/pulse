namespace NetEvolve.Pulse.Interceptors;

using System;
using global::Polly;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Event interceptor that applies Polly resilience policies to event handlers.
/// Integrates Polly v8 ResiliencePipeline with the Pulse event handling pipeline,
/// enabling retry, circuit breaker, timeout, and bulkhead strategies for event processing.
/// </summary>
/// <typeparam name="TEvent">The type of event to intercept, which must implement <see cref="IEvent"/>.</typeparam>
/// <remarks>
/// <para><strong>Execution Model:</strong></para>
/// This interceptor wraps event handler execution in a Polly resilience pipeline,
/// allowing policies to control retry behavior, timeouts, circuit breaking, and resource isolation
/// for event processing scenarios.
/// <para><strong>Policy Types Supported:</strong></para>
/// <list type="bullet">
/// <item><description><strong>Retry:</strong> Automatically retry failed event processing with configurable backoff</description></item>
/// <item><description><strong>Circuit Breaker:</strong> Prevent cascading failures when event handlers consistently fail</description></item>
/// <item><description><strong>Timeout:</strong> Enforce maximum execution time for event handlers</description></item>
/// <item><description><strong>Bulkhead:</strong> Limit concurrent event processing to prevent resource exhaustion</description></item>
/// </list>
/// <para><strong>Event Handling Considerations:</strong></para>
/// Since events support multiple handlers (fan-out pattern), policies apply to the entire handler execution chain.
/// Consider whether you want policies per-handler or across all handlers:
/// <list type="bullet">
/// <item><description><strong>Global policy:</strong> Wrap all handlers together (current implementation)</description></item>
/// <item><description><strong>Per-handler policy:</strong> Would require policy registration per handler type</description></item>
/// </list>
/// <para><strong>⚠️ WARNING:</strong> Event interceptors should be fast. Heavy policies (aggressive retries, long timeouts)
/// can delay event processing significantly, especially when multiple handlers are involved.</para>
/// <para><strong>Best Practices:</strong></para>
/// <list type="bullet">
/// <item><description>Use shorter timeouts than request interceptors to keep event processing responsive</description></item>
/// <item><description>Consider circuit breakers for external event sinks (message queues, event stores)</description></item>
/// <item><description>Use retry sparingly - consider dead-letter queues for persistent failures</description></item>
/// <item><description>Monitor policy telemetry to detect systemic event processing issues</description></item>
/// <item><description>Consider using IEventOutbox for reliable event delivery instead of aggressive retries</description></item>
/// </list>
/// </remarks>
/// <example>
/// <para><strong>Basic usage with retry policy:</strong></para>
/// <code>
/// services.AddPulse(config => config
///     .AddEventHandler&lt;OrderCreatedEvent, SendEmailHandler&gt;()
///     .AddEventHandler&lt;OrderCreatedEvent, UpdateInventoryHandler&gt;()
///     .AddPollyEventPolicies&lt;OrderCreatedEvent&gt;(pipeline => pipeline
///         .AddRetry(new RetryStrategyOptions
///         {
///             MaxRetryAttempts = 2,
///             Delay = TimeSpan.FromMilliseconds(500),
///             BackoffType = DelayBackoffType.Linear
///         })));
/// </code>
/// <para><strong>Combined policies with timeout and circuit breaker:</strong></para>
/// <code>
/// services.AddPulse(config => config
///     .AddEventHandler&lt;PaymentProcessedEvent, NotificationHandler&gt;()
///     .AddPollyEventPolicies&lt;PaymentProcessedEvent&gt;(pipeline => pipeline
///         .AddTimeout(TimeSpan.FromSeconds(10))
///         .AddCircuitBreaker(new CircuitBreakerStrategyOptions
///         {
///             FailureRatio = 0.7,
///             MinimumThroughput = 5,
///             BreakDuration = TimeSpan.FromSeconds(15)
///         })));
/// </code>
/// </example>
public sealed class PollyEventInterceptor<TEvent> : IEventInterceptor<TEvent>
    where TEvent : IEvent
{
    private readonly ResiliencePipeline _pipeline;

    /// <summary>
    /// Initializes a new instance of the <see cref="PollyEventInterceptor{TEvent}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider to resolve keyed or global pipeline.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="serviceProvider"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no pipeline is registered for <typeparamref name="TEvent"/> or globally.</exception>
    public PollyEventInterceptor(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        // Try to resolve keyed pipeline first (per-event), then fallback to global
        _pipeline =
            serviceProvider.GetKeyedService<ResiliencePipeline>(typeof(TEvent))
            ?? serviceProvider.GetService<ResiliencePipeline>()
            ?? throw new InvalidOperationException(
                $"No ResiliencePipeline registered for {typeof(TEvent).Name} or globally."
            );
    }

    /// <summary>
    /// Executes the event handlers through the Polly resilience pipeline.
    /// </summary>
    /// <param name="message">The event message to process.</param>
    /// <param name="handler">The delegate that represents the next step in the interceptor chain, invoking all registered event handlers.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    /// <remarks>
    /// The handler execution (which may invoke multiple event handlers) is wrapped in the Polly pipeline,
    /// applying configured policies such as retry, circuit breaker, timeout, and bulkhead.
    /// If the pipeline is configured with retry, all handlers will be re-executed on failure.
    /// </remarks>
    public async Task HandleAsync(
        TEvent message,
        Func<TEvent, Task> handler,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(handler);

        await _pipeline
            .ExecuteAsync(async _ => await handler(message).ConfigureAwait(false), cancellationToken)
            .ConfigureAwait(false);
    }
}
