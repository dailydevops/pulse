namespace NetEvolve.Pulse.Extensibility;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides a fluent interface for configuring the Pulse mediator with additional capabilities and interceptors.
/// This interface is used during service registration to customize mediator behavior.
/// </summary>
/// <remarks>
/// <para><strong>Usage:</strong></para>
/// The configurator is passed as a delegate parameter to the <c>AddPulse</c> extension method,
/// allowing for fluent configuration of mediator features during service registration.
/// <para><strong>Extension Pattern:</strong></para>
/// This interface follows the builder/configurator pattern and can be extended with additional methods
/// via extension methods to add custom capabilities or third-party integrations.
/// <para><strong>Best Practices:</strong></para>
/// <list type="bullet">
/// <item><description>Configure all mediator features during startup</description></item>
/// <item><description>Use method chaining for cleaner configuration code</description></item>
/// <item><description>Add observability features (metrics, tracing) for production systems</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // Basic configuration
/// services.AddPulse(config =>
/// {
///     config.AddActivityAndMetrics();
/// });
///
/// // Custom extension method example
/// public static class MediatorConfiguratorExtensions
/// {
///     public static IMediatorConfigurator AddCustomValidation(this IMediatorConfigurator configurator)
///     {
///         // Add validation interceptors
///         return configurator;
///     }
/// }
///
/// // Using custom extensions
/// services.AddPulse(config =>
/// {
///     config
///         .AddActivityAndMetrics()
///         .AddCustomValidation();
/// });
/// </code>
/// </example>
public interface IMediatorConfigurator
{
    /// <summary>
    /// Gets the service collection for handler registration.
    /// </summary>
    /// <remarks>
    /// <para><strong>Purpose:</strong></para>
    /// Provides access to the underlying <see cref="IServiceCollection"/> to enable handler registration
    /// through extension methods. This supports both manual registration and automatic discovery patterns.
    /// <para><strong>Usage:</strong></para>
    /// This property is primarily used by extension methods to register handlers with specific lifetimes
    /// and to implement custom registration strategies (manual, assembly scanning, source-generated).
    /// <para><strong>Best Practices:</strong></para>
    /// <list type="bullet">
    /// <item><description>Use fluent extension methods for handler registration instead of direct service collection manipulation</description></item>
    /// <item><description>Consider AOT compatibility when choosing registration strategy</description></item>
    /// <item><description>Manual registration is AOT-safe and recommended for Native AOT scenarios</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Extension method using Services property
    /// public static IMediatorConfigurator AddCommandHandler&lt;TCommand, TResponse, THandler&gt;(
    ///     this IMediatorConfigurator configurator)
    ///     where TCommand : ICommand&lt;TResponse&gt;
    ///     where THandler : class, ICommandHandler&lt;TCommand, TResponse&gt;
    /// {
    ///     configurator.Services.AddScoped&lt;ICommandHandler&lt;TCommand, TResponse&gt;, THandler&gt;();
    ///     return configurator;
    /// }
    /// </code>
    /// </example>
    IServiceCollection Services { get; }

    /// <summary>
    /// Adds activity tracing and metrics collection for all requests processed by the mediator.
    /// This enables OpenTelemetry-compatible distributed tracing and Prometheus-compatible metrics including request counts, durations, and error rates.
    /// </summary>
    /// <returns>The current configurator instance for method chaining.</returns>
    /// <remarks>
    /// <para><strong>Observability Features:</strong></para>
    /// This method registers interceptors that automatically instrument all mediator operations with:
    /// <list type="bullet">
    /// <item><description><strong>Distributed Tracing:</strong> OpenTelemetry activities for each request, enabling end-to-end request tracking</description></item>
    /// <item><description><strong>Metrics:</strong> Counters for request counts, histograms for durations, and error counters</description></item>
    /// <item><description><strong>Correlation:</strong> Automatic propagation of trace context across service boundaries</description></item>
    /// </list>
    /// <para><strong>Metrics Collected:</strong></para>
    /// <list type="bullet">
    /// <item><description>Request count by type (command, query, event)</description></item>
    /// <item><description>Request duration histograms</description></item>
    /// <item><description>Error counts by type</description></item>
    /// <item><description>Active request gauges</description></item>
    /// </list>
    /// <para><strong>Integration:</strong></para>
    /// Works seamlessly with OpenTelemetry exporters (Jaeger, Zipkin, Application Insights) and metrics systems (Prometheus, Grafana).
    /// <para><strong>⚠️ WARNING:</strong> Requires OpenTelemetry packages to be installed and configured in your application.
    /// Ensure you have configured an appropriate exporter for traces and metrics.</para>
    /// <para><strong>Performance Impact:</strong></para>
    /// Minimal overhead (~1-2% latency increase) for standard operations. Metrics are collected asynchronously
    /// and don't block request processing.
    /// </remarks>
    /// <example>
    /// <code>
    /// // In Program.cs
    /// // Configure OpenTelemetry
    /// services.AddOpenTelemetry()
    ///     .WithTracing(tracing =>
    ///     {
    ///         tracing
    ///             .AddSource("NetEvolve.Pulse")
    ///             .AddJaegerExporter();
    ///     })
    ///     .WithMetrics(metrics =>
    ///     {
    ///         metrics
    ///             .AddMeter("NetEvolve.Pulse")
    ///             .AddPrometheusExporter();
    ///     });
    ///
    /// // Enable Pulse metrics and tracing
    /// services.AddPulse(config =>
    /// {
    ///     config.AddActivityAndMetrics();
    /// });
    ///
    /// // All mediator operations are now traced and metered
    /// var command = new CreateOrderCommand(...);
    /// await mediator.SendAsync(command); // Automatically traced
    /// </code>
    /// </example>
    /// <seealso href="https://opentelemetry.io/docs/">OpenTelemetry Documentation</seealso>
    /// <seealso href="https://prometheus.io/docs/introduction/overview/">Prometheus Documentation</seealso>
    IMediatorConfigurator AddActivityAndMetrics();

    /// <summary>
    /// Configures a custom event dispatcher to control how events are dispatched to their handlers.
    /// This allows customization of the execution strategy (parallel, sequential, rate-limited, etc.).
    /// </summary>
    /// <typeparam name="TDispatcher">
    /// The type of event dispatcher to use. Must implement <see cref="IEventDispatcher"/>.
    /// </typeparam>
    /// <param name="lifetime">
    /// The service lifetime for the dispatcher. Defaults to <see cref="ServiceLifetime.Singleton"/>
    /// as dispatchers are typically stateless or manage their own state.
    /// </param>
    /// <returns>The current configurator instance for method chaining.</returns>
    /// <remarks>
    /// <para><strong>Default Behavior:</strong></para>
    /// If no dispatcher is configured, the mediator uses parallel dispatch by default.
    /// <para><strong>Built-in Dispatchers:</strong></para>
    /// <list type="bullet">
    /// <item><description><c>ParallelEventDispatcher</c>: Executes handlers concurrently for maximum throughput (default)</description></item>
    /// <item><description><c>SequentialEventDispatcher</c>: Executes handlers one at a time in registration order</description></item>
    /// <item><description><c>RateLimitedEventDispatcher</c>: Limits concurrent execution using a semaphore</description></item>
    /// <item><description><c>PrioritizedEventDispatcher</c>: Orders handlers by priority before sequential execution</description></item>
    /// <item><description><c>TransactionalEventDispatcher</c>: Stores events in outbox for reliable delivery</description></item>
    /// </list>
    /// <para><strong>Custom Dispatchers:</strong></para>
    /// Implement <see cref="IEventDispatcher"/> for advanced scenarios:
    /// <list type="bullet">
    /// <item><description>Custom rate-limiting algorithms</description></item>
    /// <item><description>Circuit breaker patterns</description></item>
    /// <item><description>Batching and bulk operations</description></item>
    /// </list>
    /// <para><strong>⚠️ Note:</strong></para>
    /// Calling this method multiple times replaces the previous dispatcher registration.
    /// Only one dispatcher can be active at a time.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Use sequential dispatcher for ordered execution
    /// services.AddPulse(config =>
    /// {
    ///     config.UseDefaultEventDispatcher&lt;SequentialEventDispatcher&gt;();
    /// });
    ///
    /// // Custom rate-limited dispatcher
    /// services.AddPulse(config =>
    /// {
    ///     config.UseDefaultEventDispatcher&lt;RateLimitedEventDispatcher&gt;(ServiceLifetime.Scoped);
    /// });
    /// </code>
    /// </example>
    /// <seealso cref="IEventDispatcher"/>
    IMediatorConfigurator UseDefaultEventDispatcher<TDispatcher>(ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TDispatcher : class, IEventDispatcher;

    /// <summary>
    /// Configures a custom event dispatcher using a factory delegate for custom instantiation.
    /// This allows configuring dispatchers with constructor parameters (e.g., rate limits, options).
    /// </summary>
    /// <typeparam name="TDispatcher">
    /// The type of event dispatcher to use. Must implement <see cref="IEventDispatcher"/>.
    /// </typeparam>
    /// <param name="factory">
    /// A factory delegate that receives the <see cref="IServiceProvider"/> and returns the dispatcher instance.
    /// </param>
    /// <param name="lifetime">
    /// The service lifetime for the dispatcher. Defaults to <see cref="ServiceLifetime.Singleton"/>
    /// as dispatchers are typically stateless or manage their own state.
    /// </param>
    /// <returns>The current configurator instance for method chaining.</returns>
    /// <remarks>
    /// <para><strong>Use Cases:</strong></para>
    /// <list type="bullet">
    /// <item><description>Configuring <c>RateLimitedEventDispatcher</c> with custom concurrency limits</description></item>
    /// <item><description>Creating dispatchers that require external dependencies</description></item>
    /// <item><description>Configuring dispatchers based on application settings</description></item>
    /// </list>
    /// <para><strong>⚠️ Note:</strong></para>
    /// For <see cref="ServiceLifetime.Singleton"/> dispatchers, ensure the factory creates
    /// thread-safe instances. For <see cref="ServiceLifetime.Scoped"/> dispatchers, a new
    /// instance is created per scope.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Configure RateLimitedEventDispatcher with custom concurrency
    /// services.AddPulse(config =&gt;
    /// {
    ///     config.UseDefaultEventDispatcher&lt;RateLimitedEventDispatcher&gt;(
    ///         sp =&gt; new RateLimitedEventDispatcher(maxConcurrency: 10),
    ///         ServiceLifetime.Singleton
    ///     );
    /// });
    ///
    /// // Use configuration from appsettings.json
    /// services.AddPulse(config =&gt;
    /// {
    ///     config.UseDefaultEventDispatcher&lt;RateLimitedEventDispatcher&gt;(
    ///         sp =&gt;
    ///         {
    ///             var options = sp.GetRequiredService&lt;IOptions&lt;RateLimitOptions&gt;&gt;().Value;
    ///             return new RateLimitedEventDispatcher(options.MaxConcurrency);
    ///         },
    ///         ServiceLifetime.Singleton
    ///     );
    /// });
    /// </code>
    /// </example>
    /// <seealso cref="IEventDispatcher"/>
    IMediatorConfigurator UseDefaultEventDispatcher<TDispatcher>(
        Func<IServiceProvider, TDispatcher> factory,
        ServiceLifetime lifetime = ServiceLifetime.Singleton
    )
        where TDispatcher : class, IEventDispatcher;

    /// <summary>
    /// Configures a custom event dispatcher for a specific event type using keyed services.
    /// This allows different dispatch strategies for different event types.
    /// </summary>
    /// <typeparam name="TEvent">The type of event this dispatcher handles.</typeparam>
    /// <typeparam name="TDispatcher">
    /// The type of event dispatcher to use. Must implement <see cref="IEventDispatcher"/>.
    /// </typeparam>
    /// <param name="lifetime">
    /// The service lifetime for the dispatcher. Defaults to <see cref="ServiceLifetime.Singleton"/>
    /// as dispatchers are typically stateless or manage their own state.
    /// </param>
    /// <returns>The current configurator instance for method chaining.</returns>
    /// <remarks>
    /// <para><strong>Resolution Order:</strong></para>
    /// When publishing an event, the mediator resolves dispatchers in order:
    /// <list type="number">
    /// <item><description>Keyed <c>IEventDispatcher</c> with key <c>typeof(TEvent)</c> - Event-type specific (highest priority)</description></item>
    /// <item><description>Non-keyed <c>IEventDispatcher</c> - Global dispatcher</description></item>
    /// <item><description>Default <c>ParallelEventDispatcher</c> - Built-in fallback</description></item>
    /// </list>
    /// <para><strong>Use Cases:</strong></para>
    /// <list type="bullet">
    /// <item><description>Critical events requiring sequential processing</description></item>
    /// <item><description>High-volume events needing rate limiting</description></item>
    /// <item><description>Events with specific ordering requirements</description></item>
    /// </list>
    /// <para><strong>⚠️ Note:</strong></para>
    /// Calling this method multiple times for the same event type replaces the previous registration.
    /// Uses .NET Keyed Services internally with the event type as key.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config =>
    /// {
    ///     // OrderCreatedEvent uses sequential dispatch
    ///     config.UseEventDispatcherFor&lt;OrderCreatedEvent, SequentialEventDispatcher&gt;();
    ///
    ///     // PaymentProcessedEvent uses a custom rate-limited dispatcher
    ///     config.UseEventDispatcherFor&lt;PaymentProcessedEvent, RateLimitedDispatcher&gt;();
    ///
    ///     // All other events use parallel dispatch (default)
    /// });
    /// </code>
    /// </example>
    /// <seealso cref="IEventDispatcher"/>
    IMediatorConfigurator UseEventDispatcherFor<TEvent, TDispatcher>(
        ServiceLifetime lifetime = ServiceLifetime.Singleton
    )
        where TEvent : IEvent
        where TDispatcher : class, IEventDispatcher;

    /// <summary>
    /// Configures a custom event dispatcher for a specific event type using a factory delegate.
    /// This allows configuring dispatchers with constructor parameters for specific event types.
    /// </summary>
    /// <typeparam name="TEvent">The type of event this dispatcher handles.</typeparam>
    /// <typeparam name="TDispatcher">
    /// The type of event dispatcher to use. Must implement <see cref="IEventDispatcher"/>.
    /// </typeparam>
    /// <param name="factory">
    /// A factory delegate that receives the <see cref="IServiceProvider"/> and returns the dispatcher instance.
    /// </param>
    /// <param name="lifetime">
    /// The service lifetime for the dispatcher. Defaults to <see cref="ServiceLifetime.Singleton"/>
    /// as dispatchers are typically stateless or manage their own state.
    /// </param>
    /// <returns>The current configurator instance for method chaining.</returns>
    /// <remarks>
    /// <para><strong>Use Cases:</strong></para>
    /// <list type="bullet">
    /// <item><description>High-volume events requiring custom rate limits</description></item>
    /// <item><description>Event-specific dispatchers with dependency injection</description></item>
    /// <item><description>Configuration-driven dispatch strategies per event type</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config =&gt;
    /// {
    ///     // PaymentProcessedEvent uses rate-limited dispatch with custom concurrency
    ///     config.UseEventDispatcherFor&lt;PaymentProcessedEvent, RateLimitedEventDispatcher&gt;(
    ///         sp =&gt; new RateLimitedEventDispatcher(maxConcurrency: 3),
    ///         ServiceLifetime.Singleton
    ///     );
    /// });
    /// </code>
    /// </example>
    /// <seealso cref="IEventDispatcher"/>
    IMediatorConfigurator UseEventDispatcherFor<TEvent, TDispatcher>(
        Func<IServiceProvider, TDispatcher> factory,
        ServiceLifetime lifetime = ServiceLifetime.Singleton
    )
        where TEvent : IEvent
        where TDispatcher : class, IEventDispatcher;
}
