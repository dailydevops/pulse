namespace NetEvolve.Pulse.Interceptors;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;

/// <summary>
/// Provides Pulse telemetry defaults for the Polly integration, including shared <see cref="ActivitySource"/>
/// and <see cref="Meter"/> instances as well as metric instruments for resilience telemetry bridging.
/// </summary>
/// <remarks>
/// <para>
/// This class creates instruments on the <c>"NetEvolve.Pulse"</c> meter so that all Polly resilience
/// events are visible in the same observability stream as core Pulse request/event telemetry.
/// </para>
/// <para>
/// Instruments are only initialized when the telemetry bridge is active (i.e., when both
/// <c>AddActivityAndMetrics()</c> and <c>AddPollyRequestPolicies</c>/<c>AddPollyEventPolicies</c>
/// have been configured).
/// </para>
/// </remarks>
internal static class PulsePollyDefaults
{
    /// <summary>
    /// Thread-safe store of circuit breaker states per pipeline key.
    /// Key = pipeline key (e.g., request type name); Value = circuit state (0=Closed, 1=Open, 2=HalfOpen).
    /// </summary>
    internal static readonly ConcurrentDictionary<string, int> CircuitBreakerStates = new(StringComparer.Ordinal);

    private static readonly Lazy<ActivitySource> LazyActivitySource = new(
        () => new ActivitySource("NetEvolve.Pulse", Version),
        LazyThreadSafetyMode.ExecutionAndPublication
    );

    /// <summary>
    /// Gets the <see cref="System.Diagnostics.ActivitySource"/> used for Polly resilience span creation.
    /// Uses the <c>"NetEvolve.Pulse"</c> source name to integrate with the main Pulse tracing pipeline.
    /// </summary>
    public static ActivitySource ActivitySource => LazyActivitySource.Value;

    private static readonly Lazy<Meter> LazyMeter = new(
        () => new Meter("NetEvolve.Pulse", Version),
        LazyThreadSafetyMode.ExecutionAndPublication
    );

    private static readonly Lazy<Counter<long>> LazyRetryAttemptsCounter = new(
        () =>
            LazyMeter.Value.CreateCounter<long>(
                "pulse.polly.retry.attempts",
                "attempts",
                "Cumulative retry attempts, tagged by pipeline key."
            ),
        LazyThreadSafetyMode.ExecutionAndPublication
    );

    /// <summary>
    /// Gets the counter instrument tracking cumulative retry attempts.
    /// Instrument name: <c>pulse.polly.retry.attempts</c>.
    /// </summary>
    public static Counter<long> RetryAttemptsCounter => LazyRetryAttemptsCounter.Value;

    private static readonly Lazy<Counter<long>> LazyTimeoutCounter = new(
        () =>
            LazyMeter.Value.CreateCounter<long>(
                "pulse.polly.timeout.total",
                "occurrences",
                "Cumulative timeout occurrences, tagged by pipeline key."
            ),
        LazyThreadSafetyMode.ExecutionAndPublication
    );

    /// <summary>
    /// Gets the counter instrument tracking cumulative timeout occurrences.
    /// Instrument name: <c>pulse.polly.timeout.total</c>.
    /// </summary>
    public static Counter<long> TimeoutCounter => LazyTimeoutCounter.Value;

    private static readonly Lazy<ObservableGauge<int>> LazyCircuitBreakerStateGauge = new(
        () =>
            LazyMeter.Value.CreateObservableGauge<int>(
                "pulse.polly.circuitbreaker.state",
                () =>
                    CircuitBreakerStates.Select(kvp => new Measurement<int>(
                        kvp.Value,
                        new KeyValuePair<string, object?>("pulse.polly.pipeline.key", kvp.Key)
                    )),
                "state",
                "Current circuit breaker state per pipeline (0=Closed, 1=Open, 2=HalfOpen)."
            ),
        LazyThreadSafetyMode.ExecutionAndPublication
    );

    /// <summary>
    /// Gets the observable gauge reporting current circuit breaker states per pipeline.
    /// Instrument name: <c>pulse.polly.circuitbreaker.state</c>.
    /// Values: 0 = Closed, 1 = Open, 2 = HalfOpen.
    /// </summary>
    /// <remarks>
    /// Accessing this property ensures the gauge is registered with the meter. Call it once
    /// during bridge initialization to activate circuit breaker state reporting.
    /// </remarks>
    public static ObservableGauge<int> CircuitBreakerStateGauge => LazyCircuitBreakerStateGauge.Value;

    /// <summary>
    /// Gets the assembly version string used for instrument versioning.
    /// </summary>
    public static string Version { get; } =
        typeof(PulsePollyDefaults).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
}
