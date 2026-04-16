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
        var interceptor = new TimeoutStreamQueryInterceptor<TestTimeoutStreamQuery, string>(options);
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
        var interceptor = new TimeoutStreamQueryInterceptor<TestTimeoutStreamQuery, string>(options);
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
        var interceptor = new TimeoutStreamQueryInterceptor<TestTimeoutStreamQuery, string>(options);
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
        var interceptor = new TimeoutStreamQueryInterceptor<TestTimeoutStreamQuery, string>(options);
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
        var interceptor = new TimeoutStreamQueryInterceptor<TestStreamQuery, string>(options);
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
        var interceptor = new TimeoutStreamQueryInterceptor<TestTimeoutStreamQuery, string>(options);
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
        var interceptor = new TimeoutStreamQueryInterceptor<TestTimeoutStreamQuery, string>(options);
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
        var interceptor = new TimeoutStreamQueryInterceptor<TestTimeoutStreamQuery, string>(options);
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
        var interceptor = new TimeoutStreamQueryInterceptor<TestTimeoutStreamQuery, string>(options);
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

    private static async IAsyncEnumerable<T> GenerateItems<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
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
        await Task.CompletedTask;
        throw exception;
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    private sealed record TestTimeoutStreamQuery(TimeSpan? Timeout) : IStreamQuery<string>, ITimeoutRequest
    {
        public string? CorrelationId { get; set; }
    }

    private sealed class TestStreamQuery : IStreamQuery<string>
    {
        public string? CorrelationId { get; set; }
    }
}
