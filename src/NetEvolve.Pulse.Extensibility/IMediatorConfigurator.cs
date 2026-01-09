namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Provides a fluent interface for configuring the Pulse mediator with additional capabilities and interceptors.
/// This interface is used during service registration to customize mediator behavior.
/// </summary>
public interface IMediatorConfigurator
{
    /// <summary>
    /// Adds activity tracing and metrics collection for all requests processed by the mediator.
    /// This enables OpenTelemetry-compatible distributed tracing and Prometheus-compatible metrics including request counts, durations, and error rates.
    /// </summary>
    /// <returns>The current configurator instance for method chaining.</returns>
    IMediatorConfigurator AddActivityAndMetrics();
}
