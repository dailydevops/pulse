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
public class LoggingRequestInterceptorTests
{
    private static LoggingRequestInterceptor<TRequest, TResponse> CreateInterceptor<TRequest, TResponse>(
        ILogger<LoggingRequestInterceptor<TRequest, TResponse>> logger,
        LoggingInterceptorOptions? options = null,
        TimeProvider? timeProvider = null
    )
        where TRequest : IRequest<TResponse>
    {
        var opts = Options.Create(options ?? new LoggingInterceptorOptions());
        return new LoggingRequestInterceptor<TRequest, TResponse>(logger, opts, timeProvider ?? TimeProvider.System);
    }

    [Test]
    public async Task HandleAsync_WithCommand_LogsBeginAndEndAtDebugLevel()
    {
        var logger = Mock.Logger<LoggingRequestInterceptor<TestCommand, string>>();
        var interceptor = CreateInterceptor(logger, new LoggingInterceptorOptions { LogLevel = LogLevel.Debug });
        var command = new TestCommand { CorrelationId = "corr-123" };

        var result = await interceptor.HandleAsync(command, (_, _) => Task.FromResult("ok")).ConfigureAwait(false);

        _ = await Assert.That(result).IsEqualTo("ok");
        using (Assert.Multiple())
        {
            _ = await Assert.That(logger.Entries.Count).IsGreaterThanOrEqualTo(2);
            _ = await Assert.That(logger.Entries[0].LogLevel).IsEqualTo(LogLevel.Debug);
            _ = await Assert.That(logger.Entries[1].LogLevel).IsEqualTo(LogLevel.Debug);
        }
    }

    [Test]
    public async Task HandleAsync_WithCommand_LogsBeginAndEndAtInformationLevel()
    {
        var logger = Mock.Logger<LoggingRequestInterceptor<TestCommand, string>>();
        var interceptor = CreateInterceptor(logger, new LoggingInterceptorOptions { LogLevel = LogLevel.Information });
        var command = new TestCommand();

        _ = await interceptor.HandleAsync(command, (_, _) => Task.FromResult("ok")).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(logger.Entries.Count).IsGreaterThanOrEqualTo(2);
            _ = await Assert.That(logger.Entries[0].LogLevel).IsEqualTo(LogLevel.Information);
            _ = await Assert.That(logger.Entries[1].LogLevel).IsEqualTo(LogLevel.Information);
        }
    }

    [Test]
    public async Task HandleAsync_WithQuery_LogsQueryInMessage()
    {
        var logger = Mock.Logger<LoggingRequestInterceptor<TestQuery, int>>();
        var interceptor = CreateInterceptor(logger);
        var query = new TestQuery();

        _ = await interceptor.HandleAsync(query, (_, _) => Task.FromResult(42)).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(logger.Entries[0].Message).Contains("Query");
            _ = await Assert.That(logger.Entries[0].Message).Contains("TestQuery");
        }
    }

    [Test]
    public async Task HandleAsync_WithGenericRequest_LogsRequestInMessage()
    {
        var logger = Mock.Logger<LoggingRequestInterceptor<TestRequest, bool>>();
        var interceptor = CreateInterceptor(logger);
        var request = new TestRequest();

        _ = await interceptor.HandleAsync(request, (_, _) => Task.FromResult(true)).ConfigureAwait(false);

        _ = await Assert.That(logger.Entries[0].Message).Contains("Request");
    }

    [Test]
    public async Task HandleAsync_WithSlowRequest_LogsWarning()
    {
        var logger = Mock.Logger<LoggingRequestInterceptor<TestCommand, string>>();
        var interceptor = CreateInterceptor(
            logger,
            new LoggingInterceptorOptions { SlowRequestThreshold = TimeSpan.FromMilliseconds(1) }
        );
        var command = new TestCommand();

        _ = await interceptor
            .HandleAsync(
                command,
                async (_, ct) =>
                {
                    await Task.Delay(50, ct).ConfigureAwait(false);
                    return "ok";
                }
            )
            .ConfigureAwait(false);

        var warnings = logger.Entries.Where(e => e.LogLevel == LogLevel.Warning).ToList();
        _ = await Assert.That(warnings).HasSingleItem();
        _ = await Assert.That(warnings[0].Message).Contains("threshold");
    }

    [Test]
    public async Task HandleAsync_WithDisabledSlowRequestThreshold_DoesNotLogWarning()
    {
        var logger = Mock.Logger<LoggingRequestInterceptor<TestCommand, string>>();
        var interceptor = CreateInterceptor(logger, new LoggingInterceptorOptions { SlowRequestThreshold = null });
        var command = new TestCommand();

        _ = await interceptor
            .HandleAsync(
                command,
                async (_, ct) =>
                {
                    await Task.Delay(50, ct).ConfigureAwait(false);
                    return "ok";
                }
            )
            .ConfigureAwait(false);

        var warnings = logger.Entries.Where(e => e.LogLevel == LogLevel.Warning).ToList();
        _ = await Assert.That(warnings).IsEmpty();
    }

    [Test]
    public async Task HandleAsync_WhenHandlerThrows_LogsErrorAndRethrows()
    {
        var logger = Mock.Logger<LoggingRequestInterceptor<TestCommand, string>>();
        var interceptor = CreateInterceptor(logger);
        var command = new TestCommand();
        var expectedException = new InvalidOperationException("test error");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await interceptor.HandleAsync(command, (_, _) => throw expectedException).ConfigureAwait(false)
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
    public async Task HandleAsync_LogsCorrelationId()
    {
        var logger = Mock.Logger<LoggingRequestInterceptor<TestCommand, string>>();
        var interceptor = CreateInterceptor(logger);
        var command = new TestCommand { CorrelationId = "my-correlation-id" };

        _ = await interceptor.HandleAsync(command, (_, _) => Task.FromResult("ok")).ConfigureAwait(false);

        _ = await Assert.That(logger.Entries[0].Message).Contains("my-correlation-id");
    }

    [Test]
    public async Task HandleAsync_InvokesHandlerWithCorrectRequest()
    {
        var logger = Mock.Logger<LoggingRequestInterceptor<TestCommand, string>>();
        var interceptor = CreateInterceptor(logger);
        var command = new TestCommand();
        TestCommand? received = null;

        _ = await interceptor
            .HandleAsync(
                command,
                (cmd, _) =>
                {
                    received = cmd;
                    return Task.FromResult("ok");
                }
            )
            .ConfigureAwait(false);

        _ = await Assert.That(received).IsSameReferenceAs(command);
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
