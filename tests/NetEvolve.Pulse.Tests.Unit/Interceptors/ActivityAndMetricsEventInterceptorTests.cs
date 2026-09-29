namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using TUnit.Core;

[TestGroup("Interceptors")]
public class ActivityAndMetricsEventInterceptorTests
{
    [Test]
    [NotInParallel]
    public async Task HandleAsync_CreatesActivityWithCorrectTags(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "NetEvolve.Pulse", StringComparison.Ordinal),
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var timeProvider = TimeProvider.System;
        var interceptor = new ActivityAndMetricsEventInterceptor<TestEvent>(timeProvider);
        var testEvent = new TestEvent();
        var handlerCalled = false;
        Activity? capturedActivity = null;

        listener.ActivityStarted = activity => capturedActivity = activity;

        await interceptor
            .HandleAsync(
                testEvent,
                (_, _) =>
                {
                    handlerCalled = true;
                    return Task.CompletedTask;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(handlerCalled).IsTrue();
            _ = await Assert.That(capturedActivity).IsNotNull();
            _ = await Assert.That(capturedActivity!.DisplayName).IsEqualTo("Event.TestEvent");
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.event.type")).IsEqualTo("Event");
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.event.name")).IsEqualTo("TestEvent");
        }
    }

    [Test]
    [NotInParallel]
    public async Task HandleAsync_WhenHandlerSucceeds_LeavesActivityStatusUnset(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "NetEvolve.Pulse", StringComparison.Ordinal),
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var timeProvider = TimeProvider.System;
        var interceptor = new ActivityAndMetricsEventInterceptor<TestEvent>(timeProvider);
        var testEvent = new TestEvent();
        Activity? capturedActivity = null;

        listener.ActivityStopped = activity => capturedActivity = activity;

        await interceptor.HandleAsync(testEvent, (_, _) => Task.CompletedTask, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(capturedActivity).IsNotNull();
            _ = await Assert.That(capturedActivity!.Status).IsEqualTo(ActivityStatusCode.Unset);
#pragma warning disable CS8605 // Unboxing a possibly null value.
            _ = await Assert.That((bool)capturedActivity.GetTagItem("pulse.success")).IsTrue();
#pragma warning restore CS8605 // Unboxing a possibly null value.
        }
    }

    [Test]
    [NotInParallel]
    public async Task HandleAsync_WhenHandlerThrows_SetsActivityStatusToError(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "NetEvolve.Pulse", StringComparison.Ordinal),
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var timeProvider = TimeProvider.System;
        var interceptor = new ActivityAndMetricsEventInterceptor<TestEvent>(timeProvider);
        var testEvent = new TestEvent();
        Activity? capturedActivity = null;
        var testException = new InvalidOperationException("Test exception");

        listener.ActivityStopped = activity => capturedActivity = activity;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await interceptor
                .HandleAsync(testEvent, (_, _) => throw testException, cancellationToken)
                .ConfigureAwait(false)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(exception).IsSameReferenceAs(testException);
            _ = await Assert.That(capturedActivity).IsNotNull();
            _ = await Assert.That(capturedActivity!.Status).IsEqualTo(ActivityStatusCode.Error);
            _ = await Assert.That(capturedActivity.StatusDescription).IsEqualTo("Test exception");
#pragma warning disable CS8605 // Unboxing a possibly null value.
            _ = await Assert.That((bool)capturedActivity.GetTagItem("pulse.success")).IsFalse();
#pragma warning restore CS8605 // Unboxing a possibly null value.
            _ = await Assert
                .That(capturedActivity.GetTagItem("pulse.exception.type"))
                .IsEqualTo("System.InvalidOperationException");
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.exception.message")).IsEqualTo("Test exception");
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.exception.stacktrace")).IsNotNull();
        }
    }

    [Test]
    [NotInParallel]
    public async Task HandleAsync_SetsTimestamps(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "NetEvolve.Pulse", StringComparison.Ordinal),
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var timeProvider = TimeProvider.System;
        var interceptor = new ActivityAndMetricsEventInterceptor<TestEvent>(timeProvider);
        var testEvent = new TestEvent();
        Activity? capturedActivity = null;

        listener.ActivityStopped = activity => capturedActivity = activity;

        await interceptor.HandleAsync(testEvent, (_, _) => Task.CompletedTask, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(capturedActivity).IsNotNull();
            _ = await Assert.That(capturedActivity!.GetTagItem("pulse.event.timestamp")).IsNotNull();
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.event.completion.timestamp")).IsNotNull();
        }
    }

    [Test]
    public async Task HandleAsync_InvokesHandlerWithCorrectEvent(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var timeProvider = TimeProvider.System;
        var interceptor = new ActivityAndMetricsEventInterceptor<TestEvent>(timeProvider);
        var testEvent = new TestEvent();
        TestEvent? receivedEvent = null;

        await interceptor
            .HandleAsync(
                testEvent,
                (evt, _) =>
                {
                    receivedEvent = evt;
                    return Task.CompletedTask;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(receivedEvent).IsSameReferenceAs(testEvent);
    }

    [Test]
    [NotInParallel]
    public async Task HandleAsync_WithDifferentEventTypes_CreatesCorrectActivities(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "NetEvolve.Pulse", StringComparison.Ordinal),
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var timeProvider = TimeProvider.System;
        var interceptor1 = new ActivityAndMetricsEventInterceptor<TestEvent>(timeProvider);
        var interceptor2 = new ActivityAndMetricsEventInterceptor<AnotherTestEvent>(timeProvider);
        var activities = new List<Activity>();

        listener.ActivityStarted = activity => activities.Add(activity);

        await interceptor1
            .HandleAsync(new TestEvent(), (_, _) => Task.CompletedTask, cancellationToken)
            .ConfigureAwait(false);
        await interceptor2
            .HandleAsync(new AnotherTestEvent(), (_, _) => Task.CompletedTask, cancellationToken)
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(activities.Count).IsEqualTo(2);
            _ = await Assert.That(activities[0].DisplayName).IsEqualTo("Event.TestEvent");
            _ = await Assert.That(activities[1].DisplayName).IsEqualTo("Event.AnotherTestEvent");
        }
    }

    [Test]
    [NotInParallel]
    public async Task HandleAsync_WithNullCausationId_DoesNotTagCausationId(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "NetEvolve.Pulse", StringComparison.Ordinal),
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var timeProvider = TimeProvider.System;
        var interceptor = new ActivityAndMetricsEventInterceptor<TestEvent>(timeProvider);
        var testEvent = new TestEvent { CausationId = null };
        Activity? capturedActivity = null;

        listener.ActivityStarted = activity => capturedActivity = activity;

        await interceptor.HandleAsync(testEvent, (_, _) => Task.CompletedTask, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(capturedActivity).IsNotNull();
            _ = await Assert.That(capturedActivity!.GetTagItem("pulse.causation_id")).IsNull();
        }
    }

    [Test]
    [NotInParallel]
    public async Task HandleAsync_WithNonNullCausationId_TagsCausationId(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "NetEvolve.Pulse", StringComparison.Ordinal),
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var timeProvider = TimeProvider.System;
        var interceptor = new ActivityAndMetricsEventInterceptor<TestEvent>(timeProvider);
        var testEvent = new TestEvent { CausationId = "cmd-1" };
        Activity? capturedActivity = null;

        listener.ActivityStarted = activity => capturedActivity = activity;

        await interceptor.HandleAsync(testEvent, (_, _) => Task.CompletedTask, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(capturedActivity).IsNotNull();
            _ = await Assert.That(capturedActivity!.GetTagItem("pulse.causation_id")).IsEqualTo("cmd-1");
        }
    }

    [Test]
    [NotInParallel]
    public async Task HandleAsync_WhenHandlerThrows_SetsErrorTypeOnActivityAndMetrics(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "NetEvolve.Pulse", StringComparison.Ordinal),
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        using var collector = new PulseMeasurementCollector("pulse.event.name", nameof(MeasuredEvent));

        var interceptor = new ActivityAndMetricsEventInterceptor<MeasuredEvent>(TimeProvider.System);
        Activity? capturedActivity = null;

        listener.ActivityStopped = activity =>
        {
            if (string.Equals(activity.DisplayName, "Event.MeasuredEvent", StringComparison.Ordinal))
            {
                capturedActivity = activity;
            }
        };

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await interceptor
                .HandleAsync(
                    new MeasuredEvent(),
                    (_, _) => throw new InvalidOperationException("boom"),
                    cancellationToken
                )
                .ConfigureAwait(false)
        );

        var errors = collector.For("pulse.event.errors");
        var durations = collector.For("pulse.event.duration");

        using (Assert.Multiple())
        {
            _ = await Assert.That(capturedActivity).IsNotNull();
            _ = await Assert
                .That(capturedActivity!.GetTagItem("error.type"))
                .IsEqualTo("System.InvalidOperationException");
            _ = await Assert.That(errors).Count().IsEqualTo(1);
            _ = await Assert.That(errors[0].Tags["error.type"]).IsEqualTo("System.InvalidOperationException");
            _ = await Assert.That(durations).Count().IsEqualTo(1);
            _ = await Assert.That(durations[0].Tags["error.type"]).IsEqualTo("System.InvalidOperationException");
        }
    }

    [Test]
    [NotInParallel]
    public async Task HandleAsync_WhenHandlerSucceeds_DoesNotSetErrorType(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "NetEvolve.Pulse", StringComparison.Ordinal),
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        using var collector = new PulseMeasurementCollector("pulse.event.name", nameof(MeasuredEvent));

        var interceptor = new ActivityAndMetricsEventInterceptor<MeasuredEvent>(TimeProvider.System);
        Activity? capturedActivity = null;

        listener.ActivityStopped = activity =>
        {
            if (string.Equals(activity.DisplayName, "Event.MeasuredEvent", StringComparison.Ordinal))
            {
                capturedActivity = activity;
            }
        };

        await interceptor
            .HandleAsync(new MeasuredEvent(), (_, _) => Task.CompletedTask, cancellationToken)
            .ConfigureAwait(false);

        var durations = collector.For("pulse.event.duration");

        using (Assert.Multiple())
        {
            _ = await Assert.That(capturedActivity).IsNotNull();
            _ = await Assert.That(capturedActivity!.GetTagItem("error.type")).IsNull();
            _ = await Assert.That(durations).Count().IsEqualTo(1);
            _ = await Assert.That(durations[0].Tags.ContainsKey("error.type")).IsFalse();
            _ = await Assert.That(collector.For("pulse.event.errors")).IsEmpty();
        }
    }

    [Test]
    [NotInParallel]
    public async Task HandleAsync_WithSemanticConventionUnits_RecordsSecondsAndUcumUnits(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var collector = new PulseMeasurementCollector("pulse.event.name", nameof(MeasuredEvent));
        var timeProvider = new FakeTimeProvider();
        var interceptor = new ActivityAndMetricsEventInterceptor<MeasuredEvent>(
            timeProvider,
            Options.Create(new ActivityAndMetricsOptions { UseSemanticConventionUnits = true })
        );

        await interceptor
            .HandleAsync(
                new MeasuredEvent(),
                (_, _) =>
                {
                    timeProvider.Advance(TimeSpan.FromMilliseconds(250));
                    return Task.CompletedTask;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await interceptor
                .HandleAsync(
                    new MeasuredEvent(),
                    (_, _) => throw new InvalidOperationException("boom"),
                    cancellationToken
                )
                .ConfigureAwait(false)
        );

        var durations = collector.For("pulse.event.duration");

        using (Assert.Multiple())
        {
            _ = await Assert.That(durations).Count().IsEqualTo(2);
            _ = await Assert.That(durations[0].Unit).IsEqualTo("s");
            _ = await Assert.That(durations[0].Value).IsEqualTo(0.25);
            _ = await Assert.That(collector.For("pulse.events.total")[0].Unit).IsEqualTo("{event}");
            _ = await Assert.That(collector.For("pulse.event.errors")[0].Unit).IsEqualTo("{error}");
        }
    }

    [Test]
    [NotInParallel]
    public async Task HandleAsync_WithDefaultOptions_KeepsLegacyUnits(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var collector = new PulseMeasurementCollector("pulse.event.name", nameof(MeasuredEvent));
        var interceptor = new ActivityAndMetricsEventInterceptor<MeasuredEvent>(new FakeTimeProvider());

        await interceptor
            .HandleAsync(new MeasuredEvent(), (_, _) => Task.CompletedTask, cancellationToken)
            .ConfigureAwait(false);
        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await interceptor
                .HandleAsync(
                    new MeasuredEvent(),
                    (_, _) => throw new InvalidOperationException("boom"),
                    cancellationToken
                )
                .ConfigureAwait(false)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(collector.For("pulse.event.duration")[0].Unit).IsEqualTo("ms");
            _ = await Assert.That(collector.For("pulse.events.total")[0].Unit).IsEqualTo("events");
            _ = await Assert.That(collector.For("pulse.event.errors")[0].Unit).IsEqualTo("errors");
        }
    }

    [Test]
    public async Task Constructor_CalledRepeatedly_ReusesInstruments(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var published = new ConcurrentBag<Instrument>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, _) =>
            {
                if (
                    string.Equals(instrument.Meter.Name, "NetEvolve.Pulse", StringComparison.Ordinal)
                    && string.Equals(instrument.Name, "pulse.events.total", StringComparison.Ordinal)
                    && string.Equals(instrument.Unit, "events", StringComparison.Ordinal)
                )
                {
                    published.Add(instrument);
                }
            },
        };
        listener.Start();

        _ = new ActivityAndMetricsEventInterceptor<MeasuredEvent>(TimeProvider.System);
        _ = new ActivityAndMetricsEventInterceptor<MeasuredEvent>(TimeProvider.System);
        _ = new ActivityAndMetricsEventInterceptor<TestEvent>(TimeProvider.System);

        _ = await Assert.That(published.Distinct().Count()).IsEqualTo(1);
    }

    private sealed class MeasuredEvent : IEvent
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }

        DateTimeOffset? IEvent.PublishedAt { get; set; }
    }

    private sealed class TestEvent : IEvent
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }

        DateTimeOffset? IEvent.PublishedAt { get; set; }
    }

    private sealed class AnotherTestEvent : IEvent
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();

        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }

        DateTimeOffset? IEvent.PublishedAt { get; set; }
    }
}
