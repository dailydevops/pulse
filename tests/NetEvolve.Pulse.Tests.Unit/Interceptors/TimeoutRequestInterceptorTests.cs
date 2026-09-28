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
        cancellationToken.ThrowIfCancellationRequested();

        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options, TimeProvider.System);
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
        cancellationToken.ThrowIfCancellationRequested();

        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options, TimeProvider.System);
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
        cancellationToken.ThrowIfCancellationRequested();

        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options, TimeProvider.System);
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
        cancellationToken.ThrowIfCancellationRequested();

        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options, TimeProvider.System);
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
        cancellationToken.ThrowIfCancellationRequested();

        var options = Options.Create(
            new TimeoutRequestInterceptorOptions { GlobalTimeout = TimeSpan.FromMilliseconds(1) }
        );
        var interceptor = new TimeoutRequestInterceptor<TestCommand, string>(options, TimeProvider.System);
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
        cancellationToken.ThrowIfCancellationRequested();

        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options, TimeProvider.System);
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
        cancellationToken.ThrowIfCancellationRequested();

        var options = Options.Create(new TimeoutRequestInterceptorOptions { GlobalTimeout = TimeSpan.FromSeconds(5) });
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options, TimeProvider.System);
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
        cancellationToken.ThrowIfCancellationRequested();

        var options = Options.Create(
            new TimeoutRequestInterceptorOptions { GlobalTimeout = TimeSpan.FromMilliseconds(50) }
        );
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options, TimeProvider.System);
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
        cancellationToken.ThrowIfCancellationRequested();
        // Per-request timeout (50ms) should take precedence over global (5s),
        // so the request should time out.
        var options = Options.Create(new TimeoutRequestInterceptorOptions { GlobalTimeout = TimeSpan.FromSeconds(5) });
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options, TimeProvider.System);
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
        cancellationToken.ThrowIfCancellationRequested();

        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options, TimeProvider.System);
        var command = new TestTimeoutCommand(TimeSpan.FromSeconds(5));

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await interceptor
                .HandleAsync(command, (_, _) => throw new InvalidOperationException("handler error"), cancellationToken)
                .ConfigureAwait(false)
        );

        // If CancellationTokenSource was not disposed, a subsequent test run might detect undisposed resources.
        // This test simply verifies the interceptor completes without resource-leak exceptions.
    }

    [Test]
    public async Task HandleAsync_WithTimeoutRequest_WhenDeadlineElapsedButTimerNotYetFired_ThrowsTimeoutException(
        CancellationToken cancellationToken
    )
    {
        var timeProvider = new StarvedTimeProvider();
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options, timeProvider);
        var command = new TestTimeoutCommand(TimeSpan.FromMilliseconds(50));

        var exception = await Assert.ThrowsAsync<TimeoutException>(async () =>
            await interceptor
                .HandleAsync(
                    command,
                    (_, _) => CompleteAfterAdvancing(timeProvider, TimeSpan.FromMilliseconds(250), "late"),
                    cancellationToken
                )
                .ConfigureAwait(false)
        );

        _ = await Assert.That(exception!.Message).Contains("TestTimeoutCommand");
    }

    [Test]
    public async Task HandleAsync_WithTimeoutRequest_WhenDeadlineTimerFiresBeforeClockReachesTimeout_ThrowsTimeoutException(
        CancellationToken cancellationToken
    )
    {
        var timeProvider = new StarvedTimeProvider();
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options, timeProvider);
        var command = new TestTimeoutCommand(TimeSpan.FromMilliseconds(50));

        _ = await Assert.ThrowsAsync<TimeoutException>(async () =>
            await interceptor
                .HandleAsync(
                    command,
                    (_, ct) => CompleteAfterFiringTimers(timeProvider, TimeSpan.FromMilliseconds(49), "late", ct),
                    cancellationToken
                )
                .ConfigureAwait(false)
        );
    }

    [Test]
    public async Task HandleAsync_WithTimeoutRequest_WhenCompletesBeforeDeadline_WithStarvedTimeProvider_ReturnsResult(
        CancellationToken cancellationToken
    )
    {
        var timeProvider = new StarvedTimeProvider();
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options, timeProvider);
        var command = new TestTimeoutCommand(TimeSpan.FromMilliseconds(50));

        var result = await interceptor
            .HandleAsync(
                command,
                (_, _) => CompleteAfterAdvancing(timeProvider, TimeSpan.FromMilliseconds(49), "success"),
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(result).IsEqualTo("success");
    }

    [Test]
    public async Task HandleAsync_WithTimeoutQuery_WhenDeadlineElapsedButTimerNotYetFired_ThrowsTimeoutException(
        CancellationToken cancellationToken
    )
    {
        var timeProvider = new StarvedTimeProvider();
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutQuery, string>(options, timeProvider);
        var query = new TestTimeoutQuery(TimeSpan.FromMilliseconds(50));

        var exception = await Assert.ThrowsAsync<TimeoutException>(async () =>
            await interceptor
                .HandleAsync(
                    query,
                    (_, _) => CompleteAfterAdvancing(timeProvider, TimeSpan.FromMilliseconds(250), "late"),
                    cancellationToken
                )
                .ConfigureAwait(false)
        );

        _ = await Assert.That(exception!.Message).Contains("TestTimeoutQuery");
    }

    [Test]
    public async Task HandleAsync_WithTimeoutRequest_WhenCompletesAfterDeadlineWithoutObservingCancellation_ThrowsTimeoutException(
        CancellationToken cancellationToken
    )
    {
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options, TimeProvider.System);
        var command = new TestTimeoutCommand(TimeSpan.FromMilliseconds(50));

        _ = await Assert.ThrowsAsync<TimeoutException>(async () =>
            await interceptor
                .HandleAsync(command, (_, ct) => CompleteAfterCancellation("late", ct), cancellationToken)
                .ConfigureAwait(false)
        );
    }

    [Test]
    public async Task HandleAsync_WithOriginalTokenCancelled_AfterDeadlineElapsed_ThrowsOperationCanceledException_NotTimeoutException(
        CancellationToken cancellationToken
    )
    {
        var timeProvider = new StarvedTimeProvider();
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options, timeProvider);
        var command = new TestTimeoutCommand(TimeSpan.FromMilliseconds(50));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await interceptor
                .HandleAsync(
                    command,
                    async (_, ct) =>
                    {
                        await Task.Yield();
                        timeProvider.Advance(TimeSpan.FromMilliseconds(250));
                        await cts.CancelAsync().ConfigureAwait(false);
                        ct.ThrowIfCancellationRequested();
                        return "never";
                    },
                    cts.Token
                )
                .ConfigureAwait(false)
        );

        _ = await Assert.That(exception).IsNotTypeOf<TimeoutException>();
    }

    [Test]
    public async Task HandleAsync_WithOriginalTokenCancelled_AfterDeadlineElapsed_WhenHandlerIgnoresToken_ReturnsResult(
        CancellationToken cancellationToken
    )
    {
        var timeProvider = new StarvedTimeProvider();
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options, timeProvider);
        var command = new TestTimeoutCommand(TimeSpan.FromMilliseconds(50));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var result = await interceptor
            .HandleAsync(
                command,
                async (_, _) =>
                {
                    await Task.Yield();
                    timeProvider.Advance(TimeSpan.FromMilliseconds(250));
                    await cts.CancelAsync().ConfigureAwait(false);
                    return "late";
                },
                cts.Token
            )
            .ConfigureAwait(false);

        _ = await Assert.That(result).IsEqualTo("late");
    }

    [Test]
    public async Task HandleAsync_WithTimeoutRequest_InfiniteTimeout_NeverTimesOut(CancellationToken cancellationToken)
    {
        var timeProvider = new StarvedTimeProvider();
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options, timeProvider);
        var command = new TestTimeoutCommand(Timeout.InfiniteTimeSpan);

        var result = await interceptor
            .HandleAsync(
                command,
                (_, _) => CompleteAfterAdvancing(timeProvider, TimeSpan.FromHours(1), "success"),
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(result).IsEqualTo("success");
    }

    [Test]
    public async Task Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        var options = Options.Create(new TimeoutRequestInterceptorOptions());

        _ = await Assert
            .That(() => new TimeoutRequestInterceptor<TestTimeoutCommand, string>(options, null!))
            .Throws<ArgumentNullException>();
    }

    private static async Task<T> CompleteAfterAdvancing<T>(StarvedTimeProvider timeProvider, TimeSpan elapsed, T result)
    {
        await Task.Yield();
        timeProvider.Advance(elapsed);
        return result;
    }

    /// <summary>
    /// Advances the clock to just before the deadline, then fires the deadline timer and returns the result
    /// without observing the cancellation. This models a timer whose coarser clock fires slightly before
    /// the high-resolution elapsed time reaches the timeout.
    /// </summary>
    private static async Task<T> CompleteAfterFiringTimers<T>(
        StarvedTimeProvider timeProvider,
        TimeSpan elapsed,
        T result,
        CancellationToken cancellationToken
    )
    {
        await Task.Yield();
        timeProvider.Advance(elapsed);
        timeProvider.FireTimers();

        if (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("The deadline timer did not cancel the token.");
        }

        return result;
    }

    /// <summary>
    /// Completes normally (without throwing <see cref="OperationCanceledException"/>) only once the
    /// token has been cancelled, i.e. strictly after the deadline. This models a handler that ignores
    /// the token and finishes its work late.
    /// </summary>
    private static async Task<T> CompleteAfterCancellation<T>(T result, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancellationToken.Register(() => tcs.TrySetResult()))
        {
            await tcs.Task.ConfigureAwait(false);
        }

        return result;
    }

    private sealed record TestTimeoutQuery(TimeSpan? Timeout) : IQuery<string>, ITimeoutRequest
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
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
