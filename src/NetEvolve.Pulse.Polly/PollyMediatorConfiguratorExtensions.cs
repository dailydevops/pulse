namespace NetEvolve.Pulse;

using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using Polly;

/// <summary>
/// Provides fluent extension methods for registering Polly-based resilience policies with the Pulse mediator.
/// Enables per-handler and global policy configuration for retry, circuit breaker, timeout, bulkhead, and fallback strategies.
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong></para>
/// These extension methods integrate Polly v8 resilience pipelines with the Pulse interceptor architecture,
/// allowing fine-grained control over error handling, transient fault tolerance, and resource management
/// at both the request and event handling levels.
/// <para><strong>Policy Types:</strong></para>
/// <list type="bullet">
/// <item><description><strong>Retry:</strong> Automatically retry failed operations with configurable delay and backoff</description></item>
/// <item><description><strong>Circuit Breaker:</strong> Temporarily block requests when failure threshold is reached</description></item>
/// <item><description><strong>Timeout:</strong> Enforce maximum execution time constraints</description></item>
/// <item><description><strong>Bulkhead:</strong> Limit concurrent executions to prevent resource exhaustion</description></item>
/// <item><description><strong>Fallback:</strong> Provide alternative responses on handler failure</description></item>
/// </list>
/// <para><strong>Interceptor Ordering (LIFO):</strong></para>
/// Remember that interceptors execute in reverse order of registration. Policy interceptors registered
/// last will execute first. Plan your interceptor chain accordingly:
/// <code>
/// config
///     .AddCommandHandler&lt;CreateOrder, Result, CreateOrderHandler&gt;()
///     .AddValidationInterceptor&lt;CreateOrder, Result&gt;()  // Executes third (innermost)
///     .AddPollyRequestPolicies&lt;CreateOrder, Result&gt;(...)       // Executes second
///     .AddActivityAndMetrics();                            // Executes first (outermost)
/// </code>
/// <para><strong>Performance Considerations:</strong></para>
/// <list type="bullet">
/// <item><description>Register pipelines with Singleton lifetime for optimal performance and memory usage</description></item>
/// <item><description>Polly pipelines are thread-safe and designed to be reused across requests</description></item>
/// <item><description>Use keyed services when different handlers need different policies</description></item>
/// </list>
/// <para><strong>Best Practices:</strong></para>
/// <list type="bullet">
/// <item><description>Use retry policies for transient failures (network, database connection)</description></item>
/// <item><description>Apply circuit breakers to external dependencies to prevent cascading failures</description></item>
/// <item><description>Set realistic timeouts based on expected execution time plus retry overhead</description></item>
/// <item><description>Monitor Polly telemetry to tune policy configurations</description></item>
/// <item><description>Combine policies thoughtfully - order matters (typically: timeout > retry > circuit breaker)</description></item>
/// </list>
/// </remarks>
public static class PollyMediatorConfiguratorExtensions
{
    /// <summary>
    /// Adds Polly resilience policies for a specific request type (command or query).
    /// </summary>
    /// <typeparam name="TRequest">The request type that implements <see cref="IRequest{TResponse}"/>.</typeparam>
    /// <typeparam name="TResponse">The response type produced by the request handler.</typeparam>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configure">Action to configure the Polly resilience pipeline builder.</param>
    /// <param name="lifetime">The service lifetime for the pipeline and interceptor (default: Singleton for optimal performance).</param>
    /// <returns>The configurator for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><strong>Per-Handler Policy:</strong></para>
    /// This method registers a resilience pipeline specific to the given request type, allowing
    /// fine-grained control over each handler's fault tolerance characteristics.
    /// <para><strong>Pipeline Configuration:</strong></para>
    /// The <paramref name="configure"/> action receives a <see cref="ResiliencePipelineBuilder{TResult}"/>
    /// which supports fluent configuration of multiple strategies. Strategies execute in the order they are added.
    /// <para><strong>Lifetime Management:</strong></para>
    /// Singleton lifetime (default) is recommended for performance. The pipeline is thread-safe and stateless
    /// unless you use stateful strategies like circuit breakers (which are also thread-safe).
    /// </remarks>
    /// <example>
    /// <para><strong>Simple retry policy:</strong></para>
    /// <code>
    /// config.AddPollyRequestPolicies&lt;CreateOrderCommand, OrderResult&gt;(pipeline => pipeline
    ///     .AddRetry(new RetryStrategyOptions
    ///     {
    ///         MaxRetryAttempts = 3,
    ///         Delay = TimeSpan.FromSeconds(1),
    ///         BackoffType = DelayBackoffType.Exponential
    ///     }));
    /// </code>
    /// <para><strong>Combined policies:</strong></para>
    /// <code>
    /// config.AddPollyRequestPolicies&lt;GetUserQuery, User&gt;(pipeline => pipeline
    ///     .AddTimeout(TimeSpan.FromSeconds(30))
    ///     .AddRetry(new RetryStrategyOptions
    ///     {
    ///         MaxRetryAttempts = 3,
    ///         Delay = TimeSpan.FromSeconds(2),
    ///         BackoffType = DelayBackoffType.Exponential
    ///     })
    ///     .AddCircuitBreaker(new CircuitBreakerStrategyOptions
    ///     {
    ///         FailureRatio = 0.5,
    ///         MinimumThroughput = 10,
    ///         BreakDuration = TimeSpan.FromSeconds(30)
    ///     }));
    /// </code>
    /// </example>
    public static IMediatorConfigurator AddPollyRequestPolicies<TRequest, TResponse>(
        this IMediatorConfigurator configurator,
        Action<ResiliencePipelineBuilder<TResponse>> configure,
        ServiceLifetime lifetime = ServiceLifetime.Singleton
    )
        where TRequest : IRequest<TResponse>
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(configure);

        // Remove existing interceptor registration
        var existingInterceptor = configurator.Services.FirstOrDefault(d =>
            d.ServiceType == typeof(IRequestInterceptor<TRequest, TResponse>)
            && d.ImplementationType == typeof(PollyRequestInterceptor<TRequest, TResponse>)
        );
        if (existingInterceptor is not null)
        {
            _ = configurator.Services.Remove(existingInterceptor);
        }

        // Register the resilience pipeline as keyed service with TRequest as key
        configurator.Services.TryAdd(
            new ServiceDescriptor(
                typeof(ResiliencePipeline<TResponse>),
                typeof(TRequest),
                (sp, key) =>
                {
                    var builder = new ResiliencePipelineBuilder<TResponse>();
                    configure(builder);
                    return builder.Build();
                },
                lifetime
            )
        );

        // Register the interceptor
        configurator.Services.Add(
            new ServiceDescriptor(
                typeof(IRequestInterceptor<TRequest, TResponse>),
                typeof(PollyRequestInterceptor<TRequest, TResponse>),
                lifetime
            )
        );

        return configurator;
    }

    /// <summary>
    /// Adds Polly resilience policies for a specific command type that does not return a response.
    /// </summary>
    /// <typeparam name="TCommand">The command type that implements <see cref="ICommand{TResponse}"/> with <see cref="Void"/> as the response.</typeparam>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configure">Action to configure the Polly resilience pipeline builder.</param>
    /// <param name="lifetime">The service lifetime for the pipeline and interceptor (default: Singleton).</param>
    /// <returns>The configurator for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This is a convenience overload for void commands that don't return a response.
    /// Internally delegates to <see cref="AddPollyRequestPolicies{TRequest, TResponse}"/> with <see cref="Void"/> as the response type.
    /// </remarks>
    /// <example>
    /// <code>
    /// config.AddPollyRequestPolicies&lt;DeleteOrderCommand&gt;(pipeline => pipeline
    ///     .AddRetry(new RetryStrategyOptions
    ///     {
    ///         MaxRetryAttempts = 2,
    ///         Delay = TimeSpan.FromSeconds(1)
    ///     })
    ///     .AddTimeout(TimeSpan.FromSeconds(10)));
    /// </code>
    /// </example>
    public static IMediatorConfigurator AddPollyRequestPolicies<TCommand>(
        this IMediatorConfigurator configurator,
        Action<ResiliencePipelineBuilder<Void>> configure,
        ServiceLifetime lifetime = ServiceLifetime.Singleton
    )
        where TCommand : ICommand<Void> => configurator.AddPollyRequestPolicies<TCommand, Void>(configure, lifetime);

    /// <summary>
    /// Adds Polly resilience policies for a specific event type.
    /// </summary>
    /// <typeparam name="TEvent">The event type that implements <see cref="IEvent"/>.</typeparam>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configure">Action to configure the Polly resilience pipeline builder.</param>
    /// <param name="lifetime">The service lifetime for the pipeline and interceptor (default: Singleton).</param>
    /// <returns>The configurator for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><strong>Event Handler Policies:</strong></para>
    /// <para>
    /// This method applies policies to all handlers for the specified event type. If multiple handlers
    /// are registered, they all execute within the same policy scope. If the policy triggers a retry,
    /// all handlers will be re-executed.
    /// </para>
    /// <para><strong>⚠️ WARNING:</strong> Be conservative with retry policies on events, as multiple handlers
    /// may cause amplified side effects. Consider using IEventOutbox for reliable event delivery instead
    /// of aggressive retries.</para>
    /// <para><strong>Performance:</strong></para>
    /// <para>
    /// Use shorter timeouts for events than for requests to keep event processing responsive.
    /// Event handlers should be fast by design.
    /// </para>
    /// </remarks>
    /// <example>
    /// <para><strong>Timeout and circuit breaker for event handlers:</strong></para>
    /// <code>
    /// config
    ///     .AddEventHandler&lt;OrderCreatedEvent, SendEmailHandler&gt;()
    ///     .AddEventHandler&lt;OrderCreatedEvent, UpdateInventoryHandler&gt;()
    ///     .AddPollyEventPolicies&lt;OrderCreatedEvent&gt;(pipeline => pipeline
    ///         .AddTimeout(TimeSpan.FromSeconds(10))
    ///         .AddCircuitBreaker(new CircuitBreakerStrategyOptions
    ///         {
    ///             FailureRatio = 0.7,
    ///             MinimumThroughput = 5,
    ///             BreakDuration = TimeSpan.FromSeconds(15)
    ///         }));
    /// </code>
    /// </example>
    public static IMediatorConfigurator AddPollyEventPolicies<TEvent>(
        this IMediatorConfigurator configurator,
        Action<ResiliencePipelineBuilder> configure,
        ServiceLifetime lifetime = ServiceLifetime.Singleton
    )
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(configure);

        // Remove existing interceptor registration
        var existingInterceptor = configurator.Services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventInterceptor<TEvent>)
            && d.ImplementationType == typeof(PollyEventInterceptor<TEvent>)
        );
        if (existingInterceptor is not null)
        {
            _ = configurator.Services.Remove(existingInterceptor);
        }

        // Register the resilience pipeline as keyed service with TEvent as key
        configurator.Services.TryAdd(
            new ServiceDescriptor(
                typeof(ResiliencePipeline),
                typeof(TEvent),
                (_1, _2) =>
                {
                    var builder = new ResiliencePipelineBuilder();
                    configure(builder);
                    return builder.Build();
                },
                lifetime
            )
        );

        // Register the event interceptor
        configurator.Services.Add(
            new ServiceDescriptor(typeof(IEventInterceptor<TEvent>), typeof(PollyEventInterceptor<TEvent>), lifetime)
        );

        return configurator;
    }
}
