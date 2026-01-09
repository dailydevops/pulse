namespace NetEvolve.Pulse.Tests.Unit.Internals;

using System.Diagnostics;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Internals;
using TUnit.Core;

public class ActivityAndMetricsEventInterceptorTests
{
    [Test]
    [NotInParallel]
    public async Task HandleAsync_CreatesActivityWithCorrectTags()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "NetEvolve.Pulse",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var timeProvider = TimeProvider.System;
        var interceptor = new ActivityAndMetricsEventInterceptor<TestEvent>(timeProvider);
        var testEvent = new TestEvent();
        var handlerCalled = false;
        Activity? capturedActivity = null;

        listener.ActivityStarted = activity => capturedActivity = activity;

        await interceptor.HandleAsync(
            testEvent,
            _ =>
            {
                handlerCalled = true;
                return Task.CompletedTask;
            }
        );

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
    public async Task HandleAsync_WhenHandlerSucceeds_SetsActivityStatusToOk()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "NetEvolve.Pulse",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var timeProvider = TimeProvider.System;
        var interceptor = new ActivityAndMetricsEventInterceptor<TestEvent>(timeProvider);
        var testEvent = new TestEvent();
        Activity? capturedActivity = null;

        listener.ActivityStopped = activity => capturedActivity = activity;

        await interceptor.HandleAsync(testEvent, _ => Task.CompletedTask);

        using (Assert.Multiple())
        {
            _ = await Assert.That(capturedActivity).IsNotNull();
            _ = await Assert.That(capturedActivity!.Status).IsEqualTo(ActivityStatusCode.Ok);
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.success")).IsEqualTo(true);
        }
    }

    [Test]
    public async Task HandleAsync_WhenHandlerThrows_SetsActivityStatusToError()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "NetEvolve.Pulse",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var timeProvider = TimeProvider.System;
        var interceptor = new ActivityAndMetricsEventInterceptor<TestEvent>(timeProvider);
        var testEvent = new TestEvent();
        Activity? capturedActivity = null;
        var testException = new InvalidOperationException("Test exception");

        listener.ActivityStopped = activity => capturedActivity = activity;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await interceptor.HandleAsync(testEvent, _ => throw testException)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(exception).IsSameReferenceAs(testException);
            _ = await Assert.That(capturedActivity).IsNotNull();
            _ = await Assert.That(capturedActivity!.Status).IsEqualTo(ActivityStatusCode.Error);
            _ = await Assert.That(capturedActivity.StatusDescription).IsEqualTo("Test exception");
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.success")).IsEqualTo(false);
            _ = await Assert
                .That(capturedActivity.GetTagItem("pulse.exception.type"))
                .IsEqualTo("System.InvalidOperationException");
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.exception.message")).IsEqualTo("Test exception");
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.exception.stacktrace")).IsNotNull();
        }
    }

    [Test]
    [NotInParallel]
    public async Task HandleAsync_SetsTimestamps()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "NetEvolve.Pulse",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var timeProvider = TimeProvider.System;
        var interceptor = new ActivityAndMetricsEventInterceptor<TestEvent>(timeProvider);
        var testEvent = new TestEvent();
        Activity? capturedActivity = null;

        listener.ActivityStopped = activity => capturedActivity = activity;

        await interceptor.HandleAsync(testEvent, _ => Task.CompletedTask);

        using (Assert.Multiple())
        {
            _ = await Assert.That(capturedActivity).IsNotNull();
            _ = await Assert.That(capturedActivity!.GetTagItem("pulse.event.timestamp")).IsNotNull();
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.event.completion.timestamp")).IsNotNull();
        }
    }

    [Test]
    public async Task HandleAsync_InvokesHandlerWithCorrectEvent()
    {
        var timeProvider = TimeProvider.System;
        var interceptor = new ActivityAndMetricsEventInterceptor<TestEvent>(timeProvider);
        var testEvent = new TestEvent();
        TestEvent? receivedEvent = null;

        await interceptor.HandleAsync(
            testEvent,
            evt =>
            {
                receivedEvent = evt;
                return Task.CompletedTask;
            }
        );

        _ = await Assert.That(receivedEvent).IsSameReferenceAs(testEvent);
    }

    [Test]
    [NotInParallel]
    public async Task HandleAsync_WithDifferentEventTypes_CreatesCorrectActivities()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "NetEvolve.Pulse",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var timeProvider = TimeProvider.System;
        var interceptor1 = new ActivityAndMetricsEventInterceptor<TestEvent>(timeProvider);
        var interceptor2 = new ActivityAndMetricsEventInterceptor<AnotherTestEvent>(timeProvider);
        var activities = new List<Activity>();

        listener.ActivityStarted = activity => activities.Add(activity);

        await interceptor1.HandleAsync(new TestEvent(), _ => Task.CompletedTask);
        await interceptor2.HandleAsync(new AnotherTestEvent(), _ => Task.CompletedTask);

        using (Assert.Multiple())
        {
            _ = await Assert.That(activities.Count).IsEqualTo(2);
            _ = await Assert.That(activities[0].DisplayName).IsEqualTo("Event.TestEvent");
            _ = await Assert.That(activities[1].DisplayName).IsEqualTo("Event.AnotherTestEvent");
        }
    }

    private sealed class TestEvent : IEvent
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        DateTimeOffset? IEvent.PublishedAt { get; set; }
    }

    private sealed class AnotherTestEvent : IEvent
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        DateTimeOffset? IEvent.PublishedAt { get; set; }
    }
}
