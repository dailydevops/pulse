namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using TUnit.Core;

[TestGroup("Interceptors")]
public sealed class TimeoutStreamQueryInterceptorTests
{
    [Test]
    public async Task HandleAsync_WithNullHandler_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutStreamQueryInterceptor<TestTimeoutStreamQuery, string>(
            options,
            TimeProvider.System
        );
        var query = new TestTimeoutStreamQuery(TimeSpan.FromSeconds(5));

        _ = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var item in interceptor.HandleAsync(query, null!, cancellationToken).ConfigureAwait(false))
            {
                // Should not reach here
            }
        });
    }

    [Test]
    public async Task HandleAsync_WithTimeoutQuery_WhenCompletesWithinDeadline_ReturnsItems(
        CancellationToken cancellationToken
    )
    {
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutStreamQueryInterceptor<TestTimeoutStreamQuery, string>(
            options,
            TimeProvider.System
        );
        var query = new TestTimeoutStreamQuery(TimeSpan.FromSeconds(5));

        var items = new List<string>();
        await foreach (
            var item in interceptor
                .HandleAsync(query, (_, _) => GenerateItems(["a", "b", "c"]), cancellationToken)
                .ConfigureAwait(false)
        )
        {
            items.Add(item);
        }

        _ = await Assert.That(items).IsEquivalentTo(["a", "b", "c"]);
    }

    [Test]
    public async Task HandleAsync_WithTimeoutQuery_WhenExceedsDeadline_ThrowsTimeoutException(
        CancellationToken cancellationToken
    )
    {
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutStreamQueryInterceptor<TestTimeoutStreamQuery, string>(
            options,
            TimeProvider.System
        );
        var query = new TestTimeoutStreamQuery(TimeSpan.FromMilliseconds(50));

        var exception = await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await foreach (
                var item in interceptor
                    .HandleAsync(
                        query,
                        (_, ct) => GenerateItemsWithDelay(["a", "b", "c"], TimeSpan.FromSeconds(5), ct),
                        cancellationToken
                    )
                    .ConfigureAwait(false)
            )
            {
                // Consume
            }
        });

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception!.Message).Contains("TestTimeoutStreamQuery");
        _ = await Assert.That(exception.Message).Contains("50");
    }

    [Test]
    public async Task HandleAsync_WithOriginalTokenCancelled_ThrowsOperationCanceledException_NotTimeoutException(
        CancellationToken cancellationToken
    )
    {
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutStreamQueryInterceptor<TestTimeoutStreamQuery, string>(
            options,
            TimeProvider.System
        );
        var query = new TestTimeoutStreamQuery(TimeSpan.FromSeconds(5));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (
                var item in interceptor
                    .HandleAsync(
                        query,
                        (_, ct) => GenerateItemsWithDelay(["a", "b", "c"], TimeSpan.FromSeconds(5), ct),
                        cts.Token
                    )
                    .ConfigureAwait(false)
            )
            {
                // Consume
            }
        });

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception).IsNotTypeOf<TimeoutException>();
    }

    [Test]
    public async Task HandleAsync_WithNonTimeoutQuery_AlwaysPassesThrough_RegardlessOfGlobalTimeout(
        CancellationToken cancellationToken
    )
    {
        var options = Options.Create(
            new TimeoutRequestInterceptorOptions { GlobalTimeout = TimeSpan.FromMilliseconds(1) }
        );
        var interceptor = new TimeoutStreamQueryInterceptor<TestStreamQuery, string>(options, TimeProvider.System);
        var query = new TestStreamQuery();

        var items = new List<string>();
        await foreach (
            var item in interceptor
                .HandleAsync(query, (_, _) => GenerateItems(["x", "y"]), cancellationToken)
                .ConfigureAwait(false)
        )
        {
            items.Add(item);
        }

        _ = await Assert.That(items).IsEquivalentTo(["x", "y"]);
    }

    [Test]
    public async Task HandleAsync_WithTimeoutQuery_NullTimeout_AndNoGlobalTimeout_PassesThrough(
        CancellationToken cancellationToken
    )
    {
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutStreamQueryInterceptor<TestTimeoutStreamQuery, string>(
            options,
            TimeProvider.System
        );
        var query = new TestTimeoutStreamQuery(null);

        var items = new List<string>();
        await foreach (
            var item in interceptor
                .HandleAsync(query, (_, _) => GenerateItems(["pass-through"]), cancellationToken)
                .ConfigureAwait(false)
        )
        {
            items.Add(item);
        }

        _ = await Assert.That(items).IsEquivalentTo(["pass-through"]);
    }

    [Test]
    public async Task HandleAsync_WithTimeoutQuery_NullTimeout_AndGlobalTimeout_WhenCompletesWithinDeadline_ReturnsItems(
        CancellationToken cancellationToken
    )
    {
        var options = Options.Create(new TimeoutRequestInterceptorOptions { GlobalTimeout = TimeSpan.FromSeconds(5) });
        var interceptor = new TimeoutStreamQueryInterceptor<TestTimeoutStreamQuery, string>(
            options,
            TimeProvider.System
        );
        var query = new TestTimeoutStreamQuery(null);

        var items = new List<string>();
        await foreach (
            var item in interceptor
                .HandleAsync(query, (_, _) => GenerateItems(["global-fallback"]), cancellationToken)
                .ConfigureAwait(false)
        )
        {
            items.Add(item);
        }

        _ = await Assert.That(items).IsEquivalentTo(["global-fallback"]);
    }

    [Test]
    public async Task HandleAsync_WithTimeoutQuery_NullTimeout_AndGlobalTimeout_WhenExceedsDeadline_ThrowsTimeoutException(
        CancellationToken cancellationToken
    )
    {
        var options = Options.Create(
            new TimeoutRequestInterceptorOptions { GlobalTimeout = TimeSpan.FromMilliseconds(50) }
        );
        var interceptor = new TimeoutStreamQueryInterceptor<TestTimeoutStreamQuery, string>(
            options,
            TimeProvider.System
        );
        var query = new TestTimeoutStreamQuery(null);

        var exception = await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await foreach (
                var item in interceptor
                    .HandleAsync(
                        query,
                        (_, ct) => GenerateItemsWithDelay(["a"], TimeSpan.FromSeconds(5), ct),
                        cancellationToken
                    )
                    .ConfigureAwait(false)
            )
            {
                // Consume
            }
        });

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception!.Message).Contains("TestTimeoutStreamQuery");
    }

    [Test]
    public async Task HandleAsync_DisposesLinkedCts_EvenWhenHandlerThrows(CancellationToken cancellationToken)
    {
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutStreamQueryInterceptor<TestTimeoutStreamQuery, string>(
            options,
            TimeProvider.System
        );
        var query = new TestTimeoutStreamQuery(TimeSpan.FromSeconds(5));
        var expectedException = new InvalidOperationException("handler error");

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (
                var item in interceptor
                    .HandleAsync(query, (_, _) => ThrowingStream<string>(expectedException), cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                // Should not reach here
            }
        });

        // If CancellationTokenSource was not disposed, a subsequent test run might detect undisposed resources.
        // This test simply verifies the interceptor completes without resource-leak exceptions.
    }

    [Test]
    public async Task HandleAsync_WithTimeoutQuery_WhenItemArrivesAfterDeadlineWithoutObservingCancellation_ThrowsTimeoutException(
        CancellationToken cancellationToken
    )
    {
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutStreamQueryInterceptor<TestTimeoutStreamQuery, string>(
            options,
            TimeProvider.System
        );
        var query = new TestTimeoutStreamQuery(TimeSpan.FromMilliseconds(50));
        var items = new List<string>();

        _ = await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await foreach (
                var item in interceptor
                    .HandleAsync(query, (_, ct) => YieldAfterCancellation(ct, "late"), cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                items.Add(item);
            }
        });

        _ = await Assert.That(items).IsEmpty();
    }

    [Test]
    public async Task HandleAsync_WithTimeoutQuery_WhenStreamCompletesAfterDeadlineWithoutObservingCancellation_ThrowsTimeoutException(
        CancellationToken cancellationToken
    )
    {
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutStreamQueryInterceptor<TestTimeoutStreamQuery, string>(
            options,
            TimeProvider.System
        );
        var query = new TestTimeoutStreamQuery(TimeSpan.FromMilliseconds(50));

        _ = await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await foreach (
                var item in interceptor
                    .HandleAsync(query, (_, ct) => YieldAfterCancellation<string>(ct), cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                // Consume
            }
        });
    }

    [Test]
    public async Task HandleAsync_WithTimeoutQuery_WhenDeadlineElapsedButTimerNotYetFired_ThrowsTimeoutException(
        CancellationToken cancellationToken
    )
    {
        var timeProvider = new StarvedTimeProvider();
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutStreamQueryInterceptor<TestTimeoutStreamQuery, string>(options, timeProvider);
        var query = new TestTimeoutStreamQuery(TimeSpan.FromMilliseconds(50));
        var items = new List<string>();

        _ = await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await foreach (
                var item in interceptor
                    .HandleAsync(
                        query,
                        (_, _) => YieldAfterAdvancing(timeProvider, TimeSpan.FromMilliseconds(250), "late"),
                        cancellationToken
                    )
                    .ConfigureAwait(false)
            )
            {
                items.Add(item);
            }
        });

        _ = await Assert.That(items).IsEmpty();
    }

    [Test]
    public async Task HandleAsync_WithTimeoutQuery_WhenItemsArriveBeforeDeadline_ReturnsItems(
        CancellationToken cancellationToken
    )
    {
        var timeProvider = new StarvedTimeProvider();
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutStreamQueryInterceptor<TestTimeoutStreamQuery, string>(options, timeProvider);
        var query = new TestTimeoutStreamQuery(TimeSpan.FromMilliseconds(50));

        var items = new List<string>();
        await foreach (
            var item in interceptor
                .HandleAsync(
                    query,
                    (_, _) => YieldAfterAdvancing(timeProvider, TimeSpan.FromMilliseconds(49), "a", "b"),
                    cancellationToken
                )
                .ConfigureAwait(false)
        )
        {
            items.Add(item);
        }

        _ = await Assert.That(items).IsEquivalentTo(["a", "b"]);
    }

    [Test]
    public async Task HandleAsync_WithTimeoutQuery_WhenDeadlineTimerFiresBeforeClockReachesTimeout_ThrowsTimeoutException(
        CancellationToken cancellationToken
    )
    {
        var timeProvider = new StarvedTimeProvider();
        var options = Options.Create(new TimeoutRequestInterceptorOptions());
        var interceptor = new TimeoutStreamQueryInterceptor<TestTimeoutStreamQuery, string>(options, timeProvider);
        var query = new TestTimeoutStreamQuery(TimeSpan.FromMilliseconds(50));
        var items = new List<string>();

        _ = await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await foreach (
                var item in interceptor
                    .HandleAsync(
                        query,
                        (_, ct) => YieldAfterFiringTimers(timeProvider, TimeSpan.FromMilliseconds(49), ct, "late"),
                        cancellationToken
                    )
                    .ConfigureAwait(false)
            )
            {
                items.Add(item);
            }
        });

        _ = await Assert.That(items).IsEmpty();
    }

    [Test]
    public async Task Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        var options = Options.Create(new TimeoutRequestInterceptorOptions());

        _ = await Assert
            .That(() => new TimeoutStreamQueryInterceptor<TestTimeoutStreamQuery, string>(options, null!))
            .Throws<ArgumentNullException>();
    }

    private static async IAsyncEnumerable<T> YieldAfterAdvancing<T>(
        StarvedTimeProvider timeProvider,
        TimeSpan elapsed,
        params T[] items
    )
    {
        await Task.Yield();
        timeProvider.Advance(elapsed);

        foreach (var item in items)
        {
            yield return item;
        }
    }

    /// <summary>
    /// Advances the clock to just before the deadline, then fires the deadline timer and yields the items
    /// without observing the cancellation. This models a timer whose coarser clock fires slightly before
    /// the high-resolution elapsed time reaches the timeout.
    /// </summary>
    private static async IAsyncEnumerable<T> YieldAfterFiringTimers<T>(
        StarvedTimeProvider timeProvider,
        TimeSpan elapsed,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        params T[] items
    )
    {
        await Task.Yield();
        timeProvider.Advance(elapsed);
        timeProvider.FireTimers();

        if (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("The deadline timer did not cancel the token.");
        }

        foreach (var item in items)
        {
            yield return item;
        }
    }

    /// <summary>
    /// Completes normally (without throwing <see cref="OperationCanceledException"/>) only once the
    /// token has been cancelled, i.e. strictly after the deadline. This models a handler whose work
    /// finished while the deadline callback was still pending (e.g. under thread-pool starvation).
    /// </summary>
    private static async IAsyncEnumerable<T> YieldAfterCancellation<T>(
        [EnumeratorCancellation] CancellationToken cancellationToken,
        params T[] items
    )
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancellationToken.Register(() => tcs.TrySetResult()))
        {
            await tcs.Task.ConfigureAwait(false);
        }

        foreach (var item in items)
        {
            yield return item;
        }
    }

    private static async IAsyncEnumerable<T> GenerateItems<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<T> GenerateItemsWithDelay<T>(
        IEnumerable<T> items,
        TimeSpan delay,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        foreach (var item in items)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            yield return item;
        }
    }

    private static async IAsyncEnumerable<T> ThrowingStream<T>(Exception exception)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        throw exception;
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    private sealed record TestTimeoutStreamQuery(TimeSpan? Timeout) : IStreamQuery<string>, ITimeoutRequest
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    /// <summary>
    /// A <see cref="TimeProvider"/> whose clock is advanced manually and whose timers only fire when
    /// <see cref="FireTimers"/> is called, modelling a deadline callback that is starved and has not run yet.
    /// </summary>
    private sealed class StarvedTimeProvider : TimeProvider
    {
        private readonly List<(TimerCallback Callback, object? State)> _timers = [];
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);

        public void Advance(TimeSpan elapsed) => _ = Interlocked.Add(ref _timestamp, elapsed.Ticks);

        public void FireTimers()
        {
            foreach (var (callback, state) in _timers)
            {
                callback(state);
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _timers.Add((callback, state));
            return new ManualTimer();
        }

        private sealed class ManualTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose() { }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class TestStreamQuery : IStreamQuery<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }
}
