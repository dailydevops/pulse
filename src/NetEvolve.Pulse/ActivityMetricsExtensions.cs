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
    /// <remarks>
    /// Metrics keep their legacy units by default. Use
    /// <see cref="AddActivityAndMetrics(IMediatorBuilder, Action{ActivityAndMetricsOptions})"/> to opt into the
    /// units of the OpenTelemetry semantic conventions.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    public static IMediatorBuilder AddActivityAndMetrics(this IMediatorBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder.Services.AddOptions<ActivityAndMetricsOptions>();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton(typeof(IEventInterceptor<>), typeof(ActivityAndMetricsEventInterceptor<>))
        );
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton(typeof(IRequestInterceptor<,>), typeof(ActivityAndMetricsRequestInterceptor<,>))
        );
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton(
                typeof(IStreamQueryInterceptor<,>),
                typeof(ActivityAndMetricsStreamQueryInterceptor<,>)
            )
        );

        return builder;
    }

    /// <summary>
    /// Adds activity tracing and metrics collection for all requests processed by the mediator and configures
    /// the telemetry options, for example to opt into the units of the OpenTelemetry semantic conventions.
    /// The options also apply to the metrics of the outbox processor.
    /// </summary>
    /// <param name="builder">The mediator builder.</param>
    /// <param name="configure">The action that configures the <see cref="ActivityAndMetricsOptions"/>.</param>
    /// <returns>The builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="builder"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    public static IMediatorBuilder AddActivityAndMetrics(
        this IMediatorBuilder builder,
        Action<ActivityAndMetricsOptions> configure
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        _ = builder.Services.Configure(configure);

        return builder.AddActivityAndMetrics();
    }
}
