namespace NetEvolve.Pulse.Extensibility;

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
/// <seealso cref="NetEvolve.Pulse.ServiceCollectionExtensions.AddPulse"/>
public interface IMediatorConfigurator
{
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
}
