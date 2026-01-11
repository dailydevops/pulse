namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System.Diagnostics;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using TUnit.Core;

public class ActivityAndMetricsRequestInterceptorTests
{
    [Test]
    [NotInParallel]
    public async Task HandleAsync_WithCommand_CreatesActivityWithCorrectTags()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "NetEvolve.Pulse",
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var timeProvider = TimeProvider.System;
        var interceptor = new ActivityAndMetricsRequestInterceptor<TestCommand, string>(timeProvider);
        var command = new TestCommand();
        var handlerCalled = false;
        Activity? capturedActivity = null;

        listener.ActivityStarted = activity => capturedActivity = activity;

        var result = await interceptor.HandleAsync(
            command,
            _ =>
            {
                handlerCalled = true;
                return Task.FromResult("test-result");
            }
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("test-result");
            _ = await Assert.That(handlerCalled).IsTrue();
            _ = await Assert.That(capturedActivity).IsNotNull();
            _ = await Assert.That(capturedActivity!.DisplayName).IsEqualTo("Command.TestCommand");
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.request.type")).IsEqualTo("Command");
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.request.name")).IsEqualTo("TestCommand");
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.response.type")).IsEqualTo("String");
        }
    }

    [Test]
    [NotInParallel]
    public async Task HandleAsync_WithQuery_CreatesActivityWithCorrectTags()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "NetEvolve.Pulse",
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var timeProvider = TimeProvider.System;
        var interceptor = new ActivityAndMetricsRequestInterceptor<TestQuery, int>(timeProvider);
        var query = new TestQuery();
        Activity? capturedActivity = null;

        listener.ActivityStarted = activity => capturedActivity = activity;

        var result = await interceptor.HandleAsync(query, _ => Task.FromResult(42));

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo(42);
            _ = await Assert.That(capturedActivity).IsNotNull();
            _ = await Assert.That(capturedActivity!.DisplayName).IsEqualTo("Query.TestQuery");
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.request.type")).IsEqualTo("Query");
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.request.name")).IsEqualTo("TestQuery");
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.response.type")).IsEqualTo("Int32");
        }
    }

    [Test]
    [NotInParallel]
    public async Task HandleAsync_WithGenericRequest_CreatesActivityWithCorrectTags()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "NetEvolve.Pulse",
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var timeProvider = TimeProvider.System;
        var interceptor = new ActivityAndMetricsRequestInterceptor<TestRequest, bool>(timeProvider);
        var request = new TestRequest();
        Activity? capturedActivity = null;

        listener.ActivityStarted = activity => capturedActivity = activity;

        var result = await interceptor.HandleAsync(request, _ => Task.FromResult(true));

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsTrue();
            _ = await Assert.That(capturedActivity).IsNotNull();
            _ = await Assert.That(capturedActivity!.DisplayName).IsEqualTo("Request.TestRequest");
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.request.type")).IsEqualTo("Request");
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.request.name")).IsEqualTo("TestRequest");
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.response.type")).IsEqualTo("Boolean");
        }
    }

    [Test]
    [NotInParallel]
    public async Task HandleAsync_WhenHandlerSucceeds_SetsActivityStatusToOk()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "NetEvolve.Pulse",
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var timeProvider = TimeProvider.System;
        var interceptor = new ActivityAndMetricsRequestInterceptor<TestCommand, string>(timeProvider);
        var command = new TestCommand();
        Activity? capturedActivity = null;

        listener.ActivityStopped = activity => capturedActivity = activity;

        _ = await interceptor.HandleAsync(command, _ => Task.FromResult("success"));

        using (Assert.Multiple())
        {
            _ = await Assert.That(capturedActivity).IsNotNull();
            _ = await Assert.That(capturedActivity!.Status).IsEqualTo(ActivityStatusCode.Ok);
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.success")).IsEqualTo(true);
        }
    }

    [Test]
    [NotInParallel]
    public async Task HandleAsync_WhenHandlerThrows_SetsActivityStatusToError()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "NetEvolve.Pulse",
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var timeProvider = TimeProvider.System;
        var interceptor = new ActivityAndMetricsRequestInterceptor<TestCommand, string>(timeProvider);
        var command = new TestCommand();
        Activity? capturedActivity = null;
        var testException = new InvalidOperationException("Test exception");

        listener.ActivityStopped = activity => capturedActivity = activity;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await interceptor.HandleAsync(command, _ => throw testException)
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
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var timeProvider = TimeProvider.System;
        var interceptor = new ActivityAndMetricsRequestInterceptor<TestCommand, string>(timeProvider);
        var command = new TestCommand();
        Activity? capturedActivity = null;

        listener.ActivityStopped = activity => capturedActivity = activity;

        _ = await interceptor.HandleAsync(command, _ => Task.FromResult("result"));

        using (Assert.Multiple())
        {
            _ = await Assert.That(capturedActivity).IsNotNull();
            _ = await Assert.That(capturedActivity!.GetTagItem("pulse.request.timestamp")).IsNotNull();
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.response.timestamp")).IsNotNull();
        }
    }

    [Test]
    public async Task HandleAsync_InvokesHandlerWithCorrectRequest()
    {
        var timeProvider = TimeProvider.System;
        var interceptor = new ActivityAndMetricsRequestInterceptor<TestCommand, string>(timeProvider);
        var command = new TestCommand();
        TestCommand? receivedCommand = null;

        _ = await interceptor.HandleAsync(
            command,
            cmd =>
            {
                receivedCommand = cmd;
                return Task.FromResult("result");
            }
        );

        _ = await Assert.That(receivedCommand).IsSameReferenceAs(command);
    }

    private sealed class TestCommand : ICommand<string>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed class TestQuery : IQuery<int>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed class TestRequest : IRequest<bool>
    {
        public string? CorrelationId { get; set; }
    }
}
