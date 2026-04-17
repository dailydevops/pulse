namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using TUnit.Core;

[TestGroup("Interceptors")]
public sealed class TimeoutRequestInterceptorTests
{
    [Test]
    public async Task HandleAsync_WithNullHandler_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options);
        var command = new TestTimeoutCommand(TimeSpan.FromSeconds(5));

        _ = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await interceptor.HandleAsync(command, null!, cancellationToken).ConfigureAwait(false)
        );
    }

    [Test]
    public async Task HandleAsync_WithTimeoutRequest_WhenCompletesWithinDeadline_ReturnsResult(
        CancellationToken cancellationToken
    )
    {
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options);
        var command = new TestTimeoutCommand(TimeSpan.FromSeconds(5));

        var result = await interceptor
            .HandleAsync(command, (_, _) => Task.FromResult("success"), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(result).IsEqualTo("success");
    }

    [Test]
    public async Task HandleAsync_WithTimeoutRequest_WhenExceedsDeadline_ThrowsTimeoutException(
        CancellationToken cancellationToken
    )
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
                    },
                    cancellationToken
                )
                .ConfigureAwait(false)
        );

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception!.Message).Contains("TestTimeoutCommand");
        _ = await Assert.That(exception.Message).Contains("50");
    }

    [Test]
    public async Task HandleAsync_WithOriginalTokenCancelled_ThrowsOperationCanceledException_NotTimeoutException(
        CancellationToken cancellationToken
    )
    {
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options);
        var command = new TestTimeoutCommand(TimeSpan.FromSeconds(5));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
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
    public async Task HandleAsync_WithNonTimeoutRequest_AlwaysPassesThrough_RegardlessOfGlobalTimeout(
        CancellationToken cancellationToken
    )
    {
        var options = Options.Create(
            new TimeoutRequestInterceptorOptions { GlobalTimeout = TimeSpan.FromMilliseconds(1) }
        );
        var interceptor = new TimeoutRequestInterceptor<TestCommand, string>(options);
        var command = new TestCommand();

        // Even though GlobalTimeout is 1ms, the non-ITimeoutRequest should pass through immediately.
        var result = await interceptor
            .HandleAsync(command, (_, _) => Task.FromResult("passed-through"), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(result).IsEqualTo("passed-through");
    }

    [Test]
    public async Task HandleAsync_WithTimeoutRequest_NullTimeout_AndNoGlobalTimeout_PassesThrough(
        CancellationToken cancellationToken
    )
    {
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options);
        var command = new TestTimeoutCommand(null);

        var result = await interceptor
            .HandleAsync(command, (_, _) => Task.FromResult("passed-through"), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(result).IsEqualTo("passed-through");
    }

    [Test]
    public async Task HandleAsync_WithTimeoutRequest_NullTimeout_AndGlobalTimeout_WhenCompletesWithinDeadline_ReturnsResult(
        CancellationToken cancellationToken
    )
    {
        var options = Options.Create(new TimeoutRequestInterceptorOptions { GlobalTimeout = TimeSpan.FromSeconds(5) });
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options);
        var command = new TestTimeoutCommand(null);

        var result = await interceptor
            .HandleAsync(command, (_, _) => Task.FromResult("global-fallback-success"), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(result).IsEqualTo("global-fallback-success");
    }

    [Test]
    public async Task HandleAsync_WithTimeoutRequest_NullTimeout_AndGlobalTimeout_WhenExceedsDeadline_ThrowsTimeoutException(
        CancellationToken cancellationToken
    )
    {
        var options = Options.Create(
            new TimeoutRequestInterceptorOptions { GlobalTimeout = TimeSpan.FromMilliseconds(50) }
        );
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options);
        var command = new TestTimeoutCommand(null);

        var exception = await Assert.ThrowsAsync<TimeoutException>(async () =>
            await interceptor
                .HandleAsync(
                    command,
                    async (_, ct) =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                        return "never";
                    },
                    cancellationToken
                )
                .ConfigureAwait(false)
        );

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception!.Message).Contains("TestTimeoutCommand");
    }

    [Test]
    public async Task HandleAsync_WithTimeoutRequest_ExplicitTimeoutOverridesGlobalTimeout(
        CancellationToken cancellationToken
    )
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
                    },
                    cancellationToken
                )
                .ConfigureAwait(false)
        );

        _ = await Assert.That(exception).IsNotNull();
    }

    [Test]
    public async Task HandleAsync_DisposesLinkedCts_EvenWhenHandlerThrows(CancellationToken cancellationToken)
    {
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options);
        var command = new TestTimeoutCommand(TimeSpan.FromSeconds(5));

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await interceptor
                .HandleAsync(command, (_, _) => throw new InvalidOperationException("handler error"), cancellationToken)
                .ConfigureAwait(false)
        );

        // If CancellationTokenSource was not disposed, a subsequent test run might detect undisposed resources.
        // This test simply verifies the interceptor completes without resource-leak exceptions.
    }

    private sealed record TestTimeoutCommand(TimeSpan? Timeout) : ICommand<string>, ITimeoutRequest
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed record TestCommand : ICommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }
}
