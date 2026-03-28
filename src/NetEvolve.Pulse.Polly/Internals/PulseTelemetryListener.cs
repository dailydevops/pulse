namespace NetEvolve.Pulse.Interceptors;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Telemetry;

/// <summary>
/// A Polly <see cref="TelemetryListener"/> that bridges Polly resilience events into the Pulse
/// <c>"NetEvolve.Pulse"</c> <see cref="System.Diagnostics.ActivitySource"/> and
/// <see cref="System.Diagnostics.Metrics.Meter"/>.
/// </summary>
/// <remarks>
/// <para>
/// This listener is activated automatically when both <c>AddActivityAndMetrics()</c> and
/// <c>AddPollyRequestPolicies</c>/<c>AddPollyEventPolicies</c> are registered.
/// No additional configuration is required.
/// </para>
/// <para>
/// The following metrics are emitted (all into the <c>"NetEvolve.Pulse"</c> meter):
/// <list type="bullet">
/// <item><description><c>pulse.polly.retry.attempts</c> — Counter incremented on each retry attempt.</description></item>
/// <item><description><c>pulse.polly.timeout.total</c> — Counter incremented on each timeout.</description></item>
/// <item><description><c>pulse.polly.circuitbreaker.state</c> — Observable gauge reporting current circuit state (0=Closed, 1=Open, 2=HalfOpen).</description></item>
/// </list>
/// </para>
/// <para>
/// Child activities are created under the active Pulse span for retry attempts and circuit breaker state transitions.
/// </para>
/// </remarks>
internal sealed class PulseTelemetryListener : TelemetryListener
{
    private const string OnRetry = "OnRetry";
    private const string OnCircuitOpened = "OnCircuitOpened";
    private const string OnCircuitClosed = "OnCircuitClosed";
    private const string OnCircuitHalfOpened = "OnCircuitHalfOpened";
    private const string OnTimeout = "OnTimeout";

    private readonly string _pipelineKey;
    private readonly TelemetryListener? _next;

    /// <summary>
    /// Initializes a new instance of <see cref="PulseTelemetryListener"/>.
    /// </summary>
    /// <param name="pipelineKey">
    /// A key identifying the pipeline (typically the request or event type name),
    /// used as a tag on emitted metrics and activities.
    /// </param>
    /// <param name="next">An optional existing <see cref="TelemetryListener"/> to chain after this one.</param>
    public PulseTelemetryListener(string pipelineKey, TelemetryListener? next = null)
    {
        _pipelineKey = pipelineKey;
        _next = next;

        // Ensure circuit breaker state gauge is registered when the listener is first created
        _ = PulsePollyDefaults.CircuitBreakerStateGauge;
    }

    /// <inheritdoc />
    public override void Write<TResult, TArgs>(in TelemetryEventArguments<TResult, TArgs> args)
    {
        HandleEvent<TResult, TArgs>(in args);

        _next?.Write<TResult, TArgs>(in args);
    }

    private void HandleEvent<TResult, TArgs>(in TelemetryEventArguments<TResult, TArgs> args)
    {
        switch (args.Event.EventName)
        {
            case OnRetry:
                HandleRetry<TResult, TArgs>(in args);
                break;

            case OnCircuitOpened:
                HandleCircuitOpened();
                break;

            case OnCircuitClosed:
                HandleCircuitClosed();
                break;

            case OnCircuitHalfOpened:
                HandleCircuitHalfOpened();
                break;

            case OnTimeout:
                HandleTimeout();
                break;
        }
    }

    private void HandleRetry<TResult, TArgs>(in TelemetryEventArguments<TResult, TArgs> args)
    {
        var tags = new TagList { { "pulse.polly.pipeline.key", _pipelineKey } };

        PulsePollyDefaults.RetryAttemptsCounter.Add(1, tags);

        using var activity = PulsePollyDefaults.ActivitySource.StartActivity(
            "pulse.polly.retry",
            ActivityKind.Internal,
            parentId: null,
            tags: tags
        );

        if (activity is not null && args.Arguments is OnRetryArguments<TResult> retryArgs)
        {
            _ = activity.SetTag("pulse.polly.retry.attempt", retryArgs.AttemptNumber);
        }
    }

    private void HandleCircuitOpened()
    {
        PulsePollyDefaults.CircuitBreakerStates[_pipelineKey] = (int)CircuitState.Open;

        using var activity = PulsePollyDefaults.ActivitySource.StartActivity(
            "pulse.polly.circuitbreaker.opened",
            ActivityKind.Internal,
            parentId: null,
            tags: new TagList { { "pulse.polly.pipeline.key", _pipelineKey } }
        );
    }

    private void HandleCircuitClosed()
    {
        PulsePollyDefaults.CircuitBreakerStates[_pipelineKey] = (int)CircuitState.Closed;

        using var activity = PulsePollyDefaults.ActivitySource.StartActivity(
            "pulse.polly.circuitbreaker.closed",
            ActivityKind.Internal,
            parentId: null,
            tags: new TagList { { "pulse.polly.pipeline.key", _pipelineKey } }
        );
    }

    private void HandleCircuitHalfOpened()
    {
        PulsePollyDefaults.CircuitBreakerStates[_pipelineKey] = (int)CircuitState.HalfOpen;

        using var activity = PulsePollyDefaults.ActivitySource.StartActivity(
            "pulse.polly.circuitbreaker.halfopened",
            ActivityKind.Internal,
            parentId: null,
            tags: new TagList { { "pulse.polly.pipeline.key", _pipelineKey } }
        );
    }

    private void HandleTimeout() =>
        PulsePollyDefaults.TimeoutCounter.Add(1, new TagList { { "pulse.polly.pipeline.key", _pipelineKey } });
}
