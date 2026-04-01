namespace NetEvolve.Pulse;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;

/// <summary>
/// Provides extension methods for registering activity tracing and metrics interceptors
/// with the Pulse mediator.
/// </summary>
public static class ActivityMetricsExtensions
{
    /// <summary>
    /// Adds activity tracing and metrics collection for all requests processed by the mediator.
    /// This enables OpenTelemetry-compatible distributed tracing and Prometheus-compatible metrics
    /// including request counts, durations, and error rates.
    /// </summary>
    /// <param name="builder">The mediator builder.</param>
    /// <returns>The builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    public static IMediatorBuilder AddActivityAndMetrics(this IMediatorBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton(typeof(IEventInterceptor<>), typeof(ActivityAndMetricsEventInterceptor<>))
        );
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton(typeof(IRequestInterceptor<,>), typeof(ActivityAndMetricsRequestInterceptor<,>))
        );

        return builder;
    }
}
