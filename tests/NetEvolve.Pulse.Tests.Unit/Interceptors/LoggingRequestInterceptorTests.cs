namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using TUnit.Core;

public class LoggingRequestInterceptorTests
{
    private static LoggingRequestInterceptor<TRequest, TResponse> CreateInterceptor<TRequest, TResponse>(
        CapturingLogger<LoggingRequestInterceptor<TRequest, TResponse>> logger,
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
        var logger = new CapturingLogger<LoggingRequestInterceptor<TestCommand, string>>();
        var interceptor = CreateInterceptor<TestCommand, string>(
            logger,
            new LoggingInterceptorOptions { LogLevel = LogLevel.Debug }
        );
        var command = new TestCommand { CorrelationId = "corr-123" };

        var result = await interceptor.HandleAsync(command, (_, _) => Task.FromResult("ok")).ConfigureAwait(false);

        _ = await Assert.That(result).IsEqualTo("ok");
        using (Assert.Multiple())
        {
            _ = await Assert.That(logger.Entries.Count).IsGreaterThanOrEqualTo(2);
            _ = await Assert.That(logger.Entries[0].Level).IsEqualTo(LogLevel.Debug);
            _ = await Assert.That(logger.Entries[1].Level).IsEqualTo(LogLevel.Debug);
        }
    }

    [Test]
    public async Task HandleAsync_WithCommand_LogsBeginAndEndAtInformationLevel()
    {
        var logger = new CapturingLogger<LoggingRequestInterceptor<TestCommand, string>>();
        var interceptor = CreateInterceptor<TestCommand, string>(
            logger,
            new LoggingInterceptorOptions { LogLevel = LogLevel.Information }
        );
        var command = new TestCommand();

        _ = await interceptor.HandleAsync(command, (_, _) => Task.FromResult("ok")).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(logger.Entries.Count).IsGreaterThanOrEqualTo(2);
            _ = await Assert.That(logger.Entries[0].Level).IsEqualTo(LogLevel.Information);
            _ = await Assert.That(logger.Entries[1].Level).IsEqualTo(LogLevel.Information);
        }
    }

    [Test]
    public async Task HandleAsync_WithQuery_LogsQueryInMessage()
    {
        var logger = new CapturingLogger<LoggingRequestInterceptor<TestQuery, int>>();
        var interceptor = CreateInterceptor<TestQuery, int>(logger);
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
        var logger = new CapturingLogger<LoggingRequestInterceptor<TestRequest, bool>>();
        var interceptor = CreateInterceptor<TestRequest, bool>(logger);
        var request = new TestRequest();

        _ = await interceptor.HandleAsync(request, (_, _) => Task.FromResult(true)).ConfigureAwait(false);

        _ = await Assert.That(logger.Entries[0].Message).Contains("Request");
    }

    [Test]
    public async Task HandleAsync_WithSlowRequest_LogsWarning()
    {
        var logger = new CapturingLogger<LoggingRequestInterceptor<TestCommand, string>>();
        var interceptor = CreateInterceptor<TestCommand, string>(
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

        var warnings = logger.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        _ = await Assert.That(warnings).HasSingleItem();
        _ = await Assert.That(warnings[0].Message).Contains("threshold");
    }

    [Test]
    public async Task HandleAsync_WithDisabledSlowRequestThreshold_DoesNotLogWarning()
    {
        var logger = new CapturingLogger<LoggingRequestInterceptor<TestCommand, string>>();
        var interceptor = CreateInterceptor<TestCommand, string>(
            logger,
            new LoggingInterceptorOptions { SlowRequestThreshold = null }
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

        var warnings = logger.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        _ = await Assert.That(warnings).IsEmpty();
    }

    [Test]
    public async Task HandleAsync_WhenHandlerThrows_LogsErrorAndRethrows()
    {
        var logger = new CapturingLogger<LoggingRequestInterceptor<TestCommand, string>>();
        var interceptor = CreateInterceptor<TestCommand, string>(logger);
        var command = new TestCommand();
        var expectedException = new InvalidOperationException("test error");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await interceptor.HandleAsync(command, (_, _) => throw expectedException).ConfigureAwait(false)
        );

        _ = await Assert.That(exception).IsSameReferenceAs(expectedException);

        var errors = logger.Entries.Where(e => e.Level == LogLevel.Error).ToList();
        using (Assert.Multiple())
        {
            _ = await Assert.That(errors).HasSingleItem();
            _ = await Assert.That(errors[0].Exception).IsSameReferenceAs(expectedException);
        }
    }

    [Test]
    public async Task HandleAsync_LogsCorrelationId()
    {
        var logger = new CapturingLogger<LoggingRequestInterceptor<TestCommand, string>>();
        var interceptor = CreateInterceptor<TestCommand, string>(logger);
        var command = new TestCommand { CorrelationId = "my-correlation-id" };

        _ = await interceptor.HandleAsync(command, (_, _) => Task.FromResult("ok")).ConfigureAwait(false);

        _ = await Assert.That(logger.Entries[0].Message).Contains("my-correlation-id");
    }

    [Test]
    public async Task HandleAsync_InvokesHandlerWithCorrectRequest()
    {
        var logger = new CapturingLogger<LoggingRequestInterceptor<TestCommand, string>>();
        var interceptor = CreateInterceptor<TestCommand, string>(logger);
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
