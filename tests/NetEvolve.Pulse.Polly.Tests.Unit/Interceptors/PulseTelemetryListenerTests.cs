namespace NetEvolve.Pulse.Polly.Tests.Unit.Interceptors;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.Tasks;
using global::Polly;
using global::Polly.CircuitBreaker;
using global::Polly.Retry;
using global::Polly.Telemetry;
using global::Polly.Timeout;
using NetEvolve.Pulse.Interceptors;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// Unit tests for <see cref="PulseTelemetryListener"/> verifying that Polly telemetry events are
/// forwarded to the Pulse <c>"NetEvolve.Pulse"</c> meter and activity source.
/// </summary>
public sealed class PulseTelemetryListenerTests
{
    [Test]
    public async Task Write_OnRetry_IncrementsRetryAttemptsCounter()
    {
        // Arrange
        var pipelineKey = $"RetryCounter_{Guid.NewGuid():N}";
        var retryCount = 0L;

        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == "pulse.polly.retry.attempts")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>(
            (instrument, measurement, _, _) =>
            {
                if (instrument.Name == "pulse.polly.retry.attempts")
                {
                    retryCount += measurement;
                }
            }
        );
        meterListener.Start();

        var listener = new PulseTelemetryListener(pipelineKey);
        var args = CreateRetryArgs(pipelineKey, attemptNumber: 1);

        // Act
        listener.Write<string, OnRetryArguments<string>>(in args);

        // Assert – MeterListener callbacks are synchronous so retryCount is updated inline
        _ = await Assert.That(retryCount).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task Write_OnRetry_CreatesChildActivity()
    {
        // Arrange
        var pipelineKey = $"RetryActivity_{Guid.NewGuid():N}";
        Activity? capturedActivity = null;

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "NetEvolve.Pulse",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (
                    activity.OperationName == "pulse.polly.retry"
                    && activity.GetTagItem("pulse.polly.pipeline.key") as string == pipelineKey
                )
                {
                    capturedActivity = activity;
                }
            },
        };
        ActivitySource.AddActivityListener(activityListener);

        var listener = new PulseTelemetryListener(pipelineKey);
        var args = CreateRetryArgs(pipelineKey, attemptNumber: 2);

        // Act
        listener.Write<string, OnRetryArguments<string>>(in args);

        // Assert – activity is captured after it stops (when using-block ends inside HandleRetry)
        _ = await Assert.That(capturedActivity).IsNotNull();
        _ = await Assert.That(capturedActivity!.GetTagItem("pulse.polly.retry.attempt")).IsEqualTo(2);
        _ = await Assert.That(capturedActivity.GetTagItem("pulse.polly.pipeline.key")).IsEqualTo(pipelineKey);
    }

    [Test]
    public async Task Write_OnCircuitOpened_UpdatesCircuitBreakerStateToOpen()
    {
        // Arrange
        var pipelineKey = $"CBOpen_{Guid.NewGuid():N}";
        var listener = new PulseTelemetryListener(pipelineKey);
        var args = CreateCircuitOpenedArgs(pipelineKey);

        // Act
        listener.Write<string, OnCircuitOpenedArguments<string>>(in args);

        // Assert
        _ = await Assert.That(PulsePollyDefaults.CircuitBreakerStates.TryGetValue(pipelineKey, out var state)).IsTrue();
        _ = await Assert.That(state).IsEqualTo((int)CircuitState.Open);
    }

    [Test]
    public async Task Write_OnCircuitOpened_CreatesChildActivity()
    {
        // Arrange
        var pipelineKey = $"CBOpenActivity_{Guid.NewGuid():N}";
        Activity? capturedActivity = null;

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "NetEvolve.Pulse",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (
                    activity.OperationName == "pulse.polly.circuitbreaker.opened"
                    && activity.GetTagItem("pulse.polly.pipeline.key") as string == pipelineKey
                )
                {
                    capturedActivity = activity;
                }
            },
        };
        ActivitySource.AddActivityListener(activityListener);

        var listener = new PulseTelemetryListener(pipelineKey);
        var args = CreateCircuitOpenedArgs(pipelineKey);

        // Act
        listener.Write<string, OnCircuitOpenedArguments<string>>(in args);

        // Assert
        _ = await Assert.That(capturedActivity).IsNotNull();
        _ = await Assert.That(capturedActivity!.GetTagItem("pulse.polly.pipeline.key")).IsEqualTo(pipelineKey);
    }

    [Test]
    public async Task Write_OnCircuitClosed_UpdatesCircuitBreakerStateToClosed()
    {
        // Arrange
        var pipelineKey = $"CBClosed_{Guid.NewGuid():N}";
        // Pre-populate state as open
        PulsePollyDefaults.CircuitBreakerStates[pipelineKey] = (int)CircuitState.Open;

        var listener = new PulseTelemetryListener(pipelineKey);
        var args = CreateCircuitClosedArgs(pipelineKey);

        // Act
        listener.Write<string, OnCircuitClosedArguments<string>>(in args);

        // Assert
        _ = await Assert.That(PulsePollyDefaults.CircuitBreakerStates.TryGetValue(pipelineKey, out var state)).IsTrue();
        _ = await Assert.That(state).IsEqualTo((int)CircuitState.Closed);
    }

    [Test]
    public async Task Write_OnCircuitClosed_CreatesChildActivity()
    {
        // Arrange
        var pipelineKey = $"CBClosedActivity_{Guid.NewGuid():N}";
        Activity? capturedActivity = null;

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "NetEvolve.Pulse",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (
                    activity.OperationName == "pulse.polly.circuitbreaker.closed"
                    && activity.GetTagItem("pulse.polly.pipeline.key") as string == pipelineKey
                )
                {
                    capturedActivity = activity;
                }
            },
        };
        ActivitySource.AddActivityListener(activityListener);

        var listener = new PulseTelemetryListener(pipelineKey);
        var args = CreateCircuitClosedArgs(pipelineKey);

        // Act
        listener.Write<string, OnCircuitClosedArguments<string>>(in args);

        // Assert
        _ = await Assert.That(capturedActivity).IsNotNull();
        _ = await Assert.That(capturedActivity!.GetTagItem("pulse.polly.pipeline.key")).IsEqualTo(pipelineKey);
    }

    [Test]
    public async Task Write_OnCircuitHalfOpened_UpdatesCircuitBreakerStateToHalfOpen()
    {
        // Arrange
        var pipelineKey = $"CBHalfOpen_{Guid.NewGuid():N}";
        PulsePollyDefaults.CircuitBreakerStates[pipelineKey] = (int)CircuitState.Open;

        var listener = new PulseTelemetryListener(pipelineKey);
        var args = CreateCircuitHalfOpenedArgs(pipelineKey);

        // Act
        listener.Write<string, OnCircuitHalfOpenedArguments>(in args);

        // Assert
        _ = await Assert.That(PulsePollyDefaults.CircuitBreakerStates.TryGetValue(pipelineKey, out var state)).IsTrue();
        _ = await Assert.That(state).IsEqualTo((int)CircuitState.HalfOpen);
    }

    [Test]
    public async Task Write_OnCircuitHalfOpened_CreatesChildActivity()
    {
        // Arrange
        var pipelineKey = $"CBHalfOpenActivity_{Guid.NewGuid():N}";
        Activity? capturedActivity = null;

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "NetEvolve.Pulse",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (
                    activity.OperationName == "pulse.polly.circuitbreaker.halfopened"
                    && activity.GetTagItem("pulse.polly.pipeline.key") as string == pipelineKey
                )
                {
                    capturedActivity = activity;
                }
            },
        };
        ActivitySource.AddActivityListener(activityListener);

        var listener = new PulseTelemetryListener(pipelineKey);
        var args = CreateCircuitHalfOpenedArgs(pipelineKey);

        // Act
        listener.Write<string, OnCircuitHalfOpenedArguments>(in args);

        // Assert
        _ = await Assert.That(capturedActivity).IsNotNull();
        _ = await Assert.That(capturedActivity!.GetTagItem("pulse.polly.pipeline.key")).IsEqualTo(pipelineKey);
    }

    [Test]
    public async Task Write_OnTimeout_IncrementsTimeoutCounter()
    {
        // Arrange
        var pipelineKey = $"Timeout_{Guid.NewGuid():N}";
        var timeoutCount = 0L;

        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == "pulse.polly.timeout.total")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>(
            (instrument, measurement, _, _) =>
            {
                if (instrument.Name == "pulse.polly.timeout.total")
                {
                    timeoutCount += measurement;
                }
            }
        );
        meterListener.Start();

        var listener = new PulseTelemetryListener(pipelineKey);
        var args = CreateTimeoutArgs(pipelineKey);

        // Act
        listener.Write<string, OnTimeoutArguments>(in args);

        // Assert
        _ = await Assert.That(timeoutCount).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task Write_UnknownEvent_DoesNotChangeAnyState()
    {
        // Arrange
        var pipelineKey = $"Unknown_{Guid.NewGuid():N}";
        var listener = new PulseTelemetryListener(pipelineKey);

        var context = ResilienceContextPool.Shared.Get();
        var source = new ResilienceTelemetrySource(pipelineKey, pipelineKey, "Unknown");
        var resilienceEvent = new ResilienceEvent(ResilienceEventSeverity.None, "UnknownEvent");
        var args = new TelemetryEventArguments<string, string>(source, resilienceEvent, context, "test", null);

        // Act – should not throw
        listener.Write<string, string>(in args);

        // Assert – pipeline key should not appear in circuit state map
        _ = await Assert.That(PulsePollyDefaults.CircuitBreakerStates.ContainsKey(pipelineKey)).IsFalse();
    }

    [Test]
    public async Task Write_ChainedListener_ForwardsToNextListener()
    {
        // Arrange
        var pipelineKey = $"Chained_{Guid.NewGuid():N}";
        var nextListenerCalled = false;
        var nextListener = new TestTelemetryListener(_ => nextListenerCalled = true);

        var listener = new PulseTelemetryListener(pipelineKey, nextListener);
        var args = CreateRetryArgs(pipelineKey, attemptNumber: 1);

        // Act
        listener.Write<string, OnRetryArguments<string>>(in args);

        // Assert
        _ = await Assert.That(nextListenerCalled).IsTrue();
    }

    [Test]
    public async Task Write_NullChainedListener_DoesNotThrow()
    {
        // Arrange
        var pipelineKey = $"NullChain_{Guid.NewGuid():N}";
        var listener = new PulseTelemetryListener(pipelineKey, next: null);
        var args = CreateRetryArgs(pipelineKey, attemptNumber: 1);

        // Act & Assert – should not throw
        _ = await Assert
            .That(() =>
            {
                listener.Write<string, OnRetryArguments<string>>(in args);
            })
            .ThrowsNothing();
    }

    // ──────────────────────────── helpers ─────────────────────────────

    private static TelemetryEventArguments<string, OnRetryArguments<string>> CreateRetryArgs(
        string pipelineKey,
        int attemptNumber
    )
    {
        var context = ResilienceContextPool.Shared.Get();
        var outcome = Outcome.FromResult("result");
        var retryArgs = new OnRetryArguments<string>(
            context,
            outcome,
            attemptNumber,
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(50)
        );
        var source = new ResilienceTelemetrySource(pipelineKey, pipelineKey, "Retry");
        var resilienceEvent = new ResilienceEvent(ResilienceEventSeverity.Warning, "OnRetry");
        return new TelemetryEventArguments<string, OnRetryArguments<string>>(
            source,
            resilienceEvent,
            context,
            retryArgs,
            outcome
        );
    }

    private static TelemetryEventArguments<string, OnCircuitOpenedArguments<string>> CreateCircuitOpenedArgs(
        string pipelineKey
    )
    {
        var context = ResilienceContextPool.Shared.Get();
        var outcome = Outcome.FromResult("result");
        var cbArgs = new OnCircuitOpenedArguments<string>(context, outcome, TimeSpan.FromSeconds(5), false);
        var source = new ResilienceTelemetrySource(pipelineKey, pipelineKey, "CircuitBreaker");
        var resilienceEvent = new ResilienceEvent(ResilienceEventSeverity.Critical, "OnCircuitOpened");
        return new TelemetryEventArguments<string, OnCircuitOpenedArguments<string>>(
            source,
            resilienceEvent,
            context,
            cbArgs,
            outcome
        );
    }

    private static TelemetryEventArguments<string, OnCircuitClosedArguments<string>> CreateCircuitClosedArgs(
        string pipelineKey
    )
    {
        var context = ResilienceContextPool.Shared.Get();
        var outcome = Outcome.FromResult("result");
        var cbArgs = new OnCircuitClosedArguments<string>(context, outcome, false);
        var source = new ResilienceTelemetrySource(pipelineKey, pipelineKey, "CircuitBreaker");
        var resilienceEvent = new ResilienceEvent(ResilienceEventSeverity.Information, "OnCircuitClosed");
        return new TelemetryEventArguments<string, OnCircuitClosedArguments<string>>(
            source,
            resilienceEvent,
            context,
            cbArgs,
            outcome
        );
    }

    private static TelemetryEventArguments<string, OnCircuitHalfOpenedArguments> CreateCircuitHalfOpenedArgs(
        string pipelineKey
    )
    {
        var context = ResilienceContextPool.Shared.Get();
        var outcome = Outcome.FromResult("result");
        var cbArgs = new OnCircuitHalfOpenedArguments(context);
        var source = new ResilienceTelemetrySource(pipelineKey, pipelineKey, "CircuitBreaker");
        var resilienceEvent = new ResilienceEvent(ResilienceEventSeverity.Warning, "OnCircuitHalfOpened");
        return new TelemetryEventArguments<string, OnCircuitHalfOpenedArguments>(
            source,
            resilienceEvent,
            context,
            cbArgs,
            outcome
        );
    }

    private static TelemetryEventArguments<string, OnTimeoutArguments> CreateTimeoutArgs(string pipelineKey)
    {
        var context = ResilienceContextPool.Shared.Get();
        var outcome = Outcome.FromResult("result");
        var timeoutArgs = new OnTimeoutArguments(context, TimeSpan.FromSeconds(30));
        var source = new ResilienceTelemetrySource(pipelineKey, pipelineKey, "Timeout");
        var resilienceEvent = new ResilienceEvent(ResilienceEventSeverity.Warning, "OnTimeout");
        return new TelemetryEventArguments<string, OnTimeoutArguments>(
            source,
            resilienceEvent,
            context,
            timeoutArgs,
            outcome
        );
    }

    /// <summary>
    /// A test-only <see cref="TelemetryListener"/> that invokes a callback on each <see cref="Write{TResult, TArgs}"/> call.
    /// </summary>
    private sealed class TestTelemetryListener : TelemetryListener
    {
        private readonly Action<string> _onWrite;

        public TestTelemetryListener(Action<string> onWrite) => _onWrite = onWrite;

        public override void Write<TResult, TArgs>(in TelemetryEventArguments<TResult, TArgs> args) =>
            _onWrite(args.Event.EventName);
    }
}
