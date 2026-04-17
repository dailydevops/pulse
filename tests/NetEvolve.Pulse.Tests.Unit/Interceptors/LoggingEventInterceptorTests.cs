namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using TUnit.Core;
using TUnit.Mocks;

[TestGroup("Interceptors")]
public class LoggingEventInterceptorTests
{
    private static LoggingEventInterceptor<TEvent> CreateInterceptor<TEvent>(
        ILogger<LoggingEventInterceptor<TEvent>> logger,
        LoggingInterceptorOptions? options = null,
        TimeProvider? timeProvider = null
    )
        where TEvent : IEvent
    {
        var opts = Options.Create(options ?? new LoggingInterceptorOptions());
        return new LoggingEventInterceptor<TEvent>(logger, opts, timeProvider ?? TimeProvider.System);
    }

    [Test]
    public async Task HandleAsync_LogsBeginAndEndAtDebugLevel(CancellationToken cancellationToken)
    {
        var logger = Mock.Logger<LoggingEventInterceptor<TestEvent>>();
        var interceptor = CreateInterceptor(logger, new LoggingInterceptorOptions { LogLevel = LogLevel.Debug });
        var testEvent = new TestEvent { CorrelationId = "corr-123" };
        var handlerCalled = false;

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
            _ = await Assert.That(logger.Entries.Count).IsGreaterThanOrEqualTo(2);
            _ = await Assert.That(logger.Entries[0].LogLevel).IsEqualTo(LogLevel.Debug);
            _ = await Assert.That(logger.Entries[1].LogLevel).IsEqualTo(LogLevel.Debug);
        }
    }

    [Test]
    public async Task HandleAsync_LogsBeginAndEndAtInformationLevel(CancellationToken cancellationToken)
    {
        var logger = Mock.Logger<LoggingEventInterceptor<TestEvent>>();
        var interceptor = CreateInterceptor(logger, new LoggingInterceptorOptions { LogLevel = LogLevel.Information });
        var testEvent = new TestEvent();

        await interceptor.HandleAsync(testEvent, (_, _) => Task.CompletedTask, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(logger.Entries.Count).IsGreaterThanOrEqualTo(2);
            _ = await Assert.That(logger.Entries[0].LogLevel).IsEqualTo(LogLevel.Information);
            _ = await Assert.That(logger.Entries[1].LogLevel).IsEqualTo(LogLevel.Information);
        }
    }

    [Test]
    public async Task HandleAsync_LogsEventNameInMessage(CancellationToken cancellationToken)
    {
        var logger = Mock.Logger<LoggingEventInterceptor<TestEvent>>();
        var interceptor = CreateInterceptor(logger);
        var testEvent = new TestEvent();

        await interceptor.HandleAsync(testEvent, (_, _) => Task.CompletedTask, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(logger.Entries[0].Message).Contains("TestEvent");
    }

    [Test]
    public async Task HandleAsync_WithSlowEvent_LogsWarning(CancellationToken cancellationToken)
    {
        var logger = Mock.Logger<LoggingEventInterceptor<TestEvent>>();
        var interceptor = CreateInterceptor(
            logger,
            new LoggingInterceptorOptions { SlowRequestThreshold = TimeSpan.FromMilliseconds(1) }
        );
        var testEvent = new TestEvent();

        await interceptor
            .HandleAsync(testEvent, async (_, ct) => await Task.Delay(50, ct).ConfigureAwait(false), cancellationToken)
            .ConfigureAwait(false);

        var warnings = logger.Entries.Where(e => e.LogLevel == LogLevel.Warning).ToList();
        _ = await Assert.That(warnings).HasSingleItem();
        _ = await Assert.That(warnings[0].Message).Contains("threshold");
    }

    [Test]
    public async Task HandleAsync_WithDisabledSlowThreshold_DoesNotLogWarning(CancellationToken cancellationToken)
    {
        var logger = Mock.Logger<LoggingEventInterceptor<TestEvent>>();
        var interceptor = CreateInterceptor(logger, new LoggingInterceptorOptions { SlowRequestThreshold = null });
        var testEvent = new TestEvent();

        await interceptor
            .HandleAsync(testEvent, async (_, ct) => await Task.Delay(50, ct).ConfigureAwait(false), cancellationToken)
            .ConfigureAwait(false);

        var warnings = logger.Entries.Where(e => e.LogLevel == LogLevel.Warning).ToList();
        _ = await Assert.That(warnings).IsEmpty();
    }

    [Test]
    public async Task HandleAsync_WhenHandlerThrows_LogsErrorAndRethrows(CancellationToken cancellationToken)
    {
        var logger = Mock.Logger<LoggingEventInterceptor<TestEvent>>();
        var interceptor = CreateInterceptor(logger);
        var testEvent = new TestEvent();
        var expectedException = new InvalidOperationException("event error");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await interceptor
                .HandleAsync(testEvent, (_, _) => throw expectedException, cancellationToken)
                .ConfigureAwait(false)
        );

        _ = await Assert.That(exception).IsSameReferenceAs(expectedException);

        var errors = logger.Entries.Where(e => e.LogLevel == LogLevel.Error).ToList();
        using (Assert.Multiple())
        {
            _ = await Assert.That(errors).HasSingleItem();
            _ = await Assert.That(errors[0].Exception).IsSameReferenceAs(expectedException);
        }
    }

    [Test]
    public async Task HandleAsync_LogsCorrelationId(CancellationToken cancellationToken)
    {
        var logger = Mock.Logger<LoggingEventInterceptor<TestEvent>>();
        var interceptor = CreateInterceptor(logger);
        var testEvent = new TestEvent { CorrelationId = "event-correlation-id" };

        await interceptor.HandleAsync(testEvent, (_, _) => Task.CompletedTask, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(logger.Entries[0].Message).Contains("event-correlation-id");
    }

    [Test]
    public async Task HandleAsync_InvokesHandlerWithCorrectEvent(CancellationToken cancellationToken)
    {
        var logger = Mock.Logger<LoggingEventInterceptor<TestEvent>>();
        var interceptor = CreateInterceptor(logger);
        var testEvent = new TestEvent();
        TestEvent? received = null;

        await interceptor
            .HandleAsync(
                testEvent,
                (evt, _) =>
                {
                    received = evt;
                    return Task.CompletedTask;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(received).IsSameReferenceAs(testEvent);
    }

    private sealed class TestEvent : IEvent
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
