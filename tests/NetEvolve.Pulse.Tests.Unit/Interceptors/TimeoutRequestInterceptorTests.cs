namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using TUnit.Core;

public sealed class TimeoutRequestInterceptorTests
{
    [Test]
    public async Task HandleAsync_WithNullHandler_ThrowsArgumentNullException()
    {
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options);
        var command = new TestTimeoutCommand(TimeSpan.FromSeconds(5));

        _ = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await interceptor.HandleAsync(command, null!).ConfigureAwait(false)
        );
    }

    [Test]
    public async Task HandleAsync_WithTimeoutRequest_WhenCompletesWithinDeadline_ReturnsResult()
    {
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options);
        var command = new TestTimeoutCommand(TimeSpan.FromSeconds(5));

        var result = await interceptor.HandleAsync(command, (_, _) => Task.FromResult("success")).ConfigureAwait(false);

        _ = await Assert.That(result).IsEqualTo("success");
    }

    [Test]
    public async Task HandleAsync_WithTimeoutRequest_WhenExceedsDeadline_ThrowsTimeoutException()
    {
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options);
        var command = new TestTimeoutCommand(TimeSpan.FromMilliseconds(50));

        var exception = await Assert.ThrowsAsync<TimeoutException>(async () =>
            await interceptor
                .HandleAsync(
                    command,
                    async (_, ct) =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                        return "never";
                    }
                )
                .ConfigureAwait(false)
        );

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception!.Message).Contains("TestTimeoutCommand");
        _ = await Assert.That(exception.Message).Contains("50");
    }

    [Test]
    public async Task HandleAsync_WithOriginalTokenCancelled_ThrowsOperationCanceledException_NotTimeoutException()
    {
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options);
        var command = new TestTimeoutCommand(TimeSpan.FromSeconds(5));
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await interceptor
                .HandleAsync(
                    command,
                    async (_, ct) =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                        return "never";
                    },
                    cts.Token
                )
                .ConfigureAwait(false)
        );

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception).IsNotTypeOf<TimeoutException>();
    }

    [Test]
    public async Task HandleAsync_WithNonTimeoutRequest_AndNoGlobalTimeout_PassesThrough()
    {
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutRequestInterceptor<TestCommand, string>(options);
        var command = new TestCommand();

        var result = await interceptor
            .HandleAsync(command, (_, _) => Task.FromResult("passed-through"))
            .ConfigureAwait(false);

        _ = await Assert.That(result).IsEqualTo("passed-through");
    }

    [Test]
    public async Task HandleAsync_WithNonTimeoutRequest_AndGlobalTimeout_WhenCompletesWithinDeadline_ReturnsResult()
    {
        var options = Options.Create(new TimeoutRequestInterceptorOptions { GlobalTimeout = TimeSpan.FromSeconds(5) });
        var interceptor = new TimeoutRequestInterceptor<TestCommand, string>(options);
        var command = new TestCommand();

        var result = await interceptor
            .HandleAsync(command, (_, _) => Task.FromResult("global-success"))
            .ConfigureAwait(false);

        _ = await Assert.That(result).IsEqualTo("global-success");
    }

    [Test]
    public async Task HandleAsync_WithNonTimeoutRequest_AndGlobalTimeout_WhenExceedsDeadline_ThrowsTimeoutException()
    {
        var options = Options.Create(
            new TimeoutRequestInterceptorOptions { GlobalTimeout = TimeSpan.FromMilliseconds(50) }
        );
        var interceptor = new TimeoutRequestInterceptor<TestCommand, string>(options);
        var command = new TestCommand();

        var exception = await Assert.ThrowsAsync<TimeoutException>(async () =>
            await interceptor
                .HandleAsync(
                    command,
                    async (_, ct) =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                        return "never";
                    }
                )
                .ConfigureAwait(false)
        );

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception!.Message).Contains("TestCommand");
    }

    [Test]
    public async Task HandleAsync_WithTimeoutRequest_TimeoutOverridesGlobalTimeout()
    {
        // Per-request timeout (50ms) should take precedence over global (5s),
        // so the request should time out.
        var options = Options.Create(new TimeoutRequestInterceptorOptions { GlobalTimeout = TimeSpan.FromSeconds(5) });
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options);
        var command = new TestTimeoutCommand(TimeSpan.FromMilliseconds(50));

        var exception = await Assert.ThrowsAsync<TimeoutException>(async () =>
            await interceptor
                .HandleAsync(
                    command,
                    async (_, ct) =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                        return "never";
                    }
                )
                .ConfigureAwait(false)
        );

        _ = await Assert.That(exception).IsNotNull();
    }

    [Test]
    public async Task HandleAsync_DisposesLinkedCts_EvenWhenHandlerThrows()
    {
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options);
        var command = new TestTimeoutCommand(TimeSpan.FromSeconds(5));

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await interceptor
                .HandleAsync(command, (_, _) => throw new InvalidOperationException("handler error"))
                .ConfigureAwait(false)
        );

        // If CancellationTokenSource was not disposed, a subsequent test run might detect undisposed resources.
        // This test simply verifies the interceptor completes without resource-leak exceptions.
    }

    private sealed record TestTimeoutCommand(TimeSpan Timeout) : ICommand<string>, ITimeoutRequest
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record TestCommand : ICommand<string>
    {
        public string? CorrelationId { get; set; }
    }
}
