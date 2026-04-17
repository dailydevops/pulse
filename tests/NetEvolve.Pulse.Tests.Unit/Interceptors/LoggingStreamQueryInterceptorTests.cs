namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using TUnit.Core;
using TUnit.Mocks;

[TestGroup("Interceptors")]
public class LoggingStreamQueryInterceptorTests
{
    private static LoggingStreamQueryInterceptor<TQuery, TResponse> CreateInterceptor<TQuery, TResponse>(
        ILogger<LoggingStreamQueryInterceptor<TQuery, TResponse>> logger,
        LoggingInterceptorOptions? options = null,
        TimeProvider? timeProvider = null
    )
        where TQuery : IStreamQuery<TResponse>
    {
        var opts = Options.Create(options ?? new LoggingInterceptorOptions());
        return new LoggingStreamQueryInterceptor<TQuery, TResponse>(logger, opts, timeProvider ?? TimeProvider.System);
    }

    [Test]
    public async Task HandleAsync_WithNormalStream_LogsBeginAndEnd(CancellationToken cancellationToken)
    {
        var logger = Mock.Logger<LoggingStreamQueryInterceptor<TestStreamQuery, string>>();
        var interceptor = CreateInterceptor(logger);
        var query = new TestStreamQuery { CorrelationId = "corr-123" };

        var items = new List<string>();
        await foreach (
            var item in interceptor
                .HandleAsync(query, (_, _) => GenerateItems(["a", "b", "c"]), cancellationToken)
                .ConfigureAwait(false)
        )
        {
            items.Add(item);
        }

        using (Assert.Multiple())
        {
            _ = await Assert.That(items).IsEquivalentTo(["a", "b", "c"]);
            _ = await Assert.That(logger.Entries.Count).IsGreaterThanOrEqualTo(2);
            _ = await Assert.That(logger.Entries[0].Message).Contains("Streaming");
            _ = await Assert.That(logger.Entries[0].Message).Contains("TestStreamQuery");
            _ = await Assert.That(logger.Entries[1].Message).Contains("Streamed");
        }
    }

    [Test]
    public async Task HandleAsync_LogsBeginAndEndAtDebugLevel(CancellationToken cancellationToken)
    {
        var logger = Mock.Logger<LoggingStreamQueryInterceptor<TestStreamQuery, string>>();
        var interceptor = CreateInterceptor(logger, new LoggingInterceptorOptions { LogLevel = LogLevel.Debug });
        var query = new TestStreamQuery();

        var items = new List<string>();
        await foreach (
            var item in interceptor
                .HandleAsync(query, (_, _) => GenerateItems(["a"]), cancellationToken)
                .ConfigureAwait(false)
        )
        {
            items.Add(item);
        }

        using (Assert.Multiple())
        {
            _ = await Assert.That(logger.Entries[0].LogLevel).IsEqualTo(LogLevel.Debug);
            _ = await Assert.That(logger.Entries[1].LogLevel).IsEqualTo(LogLevel.Debug);
        }
    }

    [Test]
    public async Task HandleAsync_LogsBeginAndEndAtInformationLevel(CancellationToken cancellationToken)
    {
        var logger = Mock.Logger<LoggingStreamQueryInterceptor<TestStreamQuery, string>>();
        var interceptor = CreateInterceptor(logger, new LoggingInterceptorOptions { LogLevel = LogLevel.Information });
        var query = new TestStreamQuery();

        var items = new List<string>();
        await foreach (
            var item in interceptor
                .HandleAsync(query, (_, _) => GenerateItems(["a"]), cancellationToken)
                .ConfigureAwait(false)
        )
        {
            items.Add(item);
        }

        using (Assert.Multiple())
        {
            _ = await Assert.That(logger.Entries[0].LogLevel).IsEqualTo(LogLevel.Information);
            _ = await Assert.That(logger.Entries[1].LogLevel).IsEqualTo(LogLevel.Information);
        }
    }

    [Test]
    public async Task HandleAsync_WithSlowStream_LogsWarning(CancellationToken cancellationToken)
    {
        var logger = Mock.Logger<LoggingStreamQueryInterceptor<TestStreamQuery, string>>();
        var interceptor = CreateInterceptor(
            logger,
            new LoggingInterceptorOptions { SlowRequestThreshold = TimeSpan.FromMilliseconds(1) }
        );
        var query = new TestStreamQuery();

        var items = new List<string>();
        await foreach (
            var item in interceptor
                .HandleAsync(query, (_, ct) => GenerateItemsWithDelay(["a", "b"], 30, ct), cancellationToken)
                .ConfigureAwait(false)
        )
        {
            items.Add(item);
        }

        var warnings = logger.Entries.Where(e => e.LogLevel == LogLevel.Warning).ToList();
        _ = await Assert.That(warnings).HasSingleItem();
        _ = await Assert.That(warnings[0].Message).Contains("threshold");
    }

    [Test]
    public async Task HandleAsync_WithDisabledSlowThreshold_DoesNotLogWarning(CancellationToken cancellationToken)
    {
        var logger = Mock.Logger<LoggingStreamQueryInterceptor<TestStreamQuery, string>>();
        var interceptor = CreateInterceptor(logger, new LoggingInterceptorOptions { SlowRequestThreshold = null });
        var query = new TestStreamQuery();

        var items = new List<string>();
        await foreach (
            var item in interceptor
                .HandleAsync(query, (_, ct) => GenerateItemsWithDelay(["a", "b"], 30, ct), cancellationToken)
                .ConfigureAwait(false)
        )
        {
            items.Add(item);
        }

        var warnings = logger.Entries.Where(e => e.LogLevel == LogLevel.Warning).ToList();
        _ = await Assert.That(warnings).IsEmpty();
    }

    [Test]
    public async Task HandleAsync_WhenHandlerThrows_LogsErrorAndRethrows(CancellationToken cancellationToken)
    {
        var logger = Mock.Logger<LoggingStreamQueryInterceptor<TestStreamQuery, string>>();
        var interceptor = CreateInterceptor(logger);
        var query = new TestStreamQuery();
        var expectedException = new InvalidOperationException("test error");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
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

        _ = await Assert.That(exception).IsSameReferenceAs(expectedException);

        var errors = logger.Entries.Where(e => e.LogLevel == LogLevel.Error).ToList();
        using (Assert.Multiple())
        {
            _ = await Assert.That(errors).HasSingleItem();
            _ = await Assert.That(errors[0].Exception).IsSameReferenceAs(expectedException);
        }
    }

    [Test]
    public async Task HandleAsync_WhenHandlerThrowsAfterYieldingItems_LogsErrorAndRethrows(
        CancellationToken cancellationToken
    )
    {
        var logger = Mock.Logger<LoggingStreamQueryInterceptor<TestStreamQuery, string>>();
        var interceptor = CreateInterceptor(logger);
        var query = new TestStreamQuery();
        var expectedException = new InvalidOperationException("error during stream");

        var receivedItems = new List<string>();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (
                var item in interceptor
                    .HandleAsync(
                        query,
                        (_, _) => GenerateItemsThenThrow(["a", "b"], expectedException),
                        cancellationToken
                    )
                    .ConfigureAwait(false)
            )
            {
                receivedItems.Add(item);
            }
        });

        using (Assert.Multiple())
        {
            _ = await Assert.That(exception).IsSameReferenceAs(expectedException);
            _ = await Assert.That(receivedItems).IsEquivalentTo(["a", "b"]);
        }

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
        var logger = Mock.Logger<LoggingStreamQueryInterceptor<TestStreamQuery, string>>();
        var interceptor = CreateInterceptor(logger);
        var query = new TestStreamQuery { CorrelationId = "my-correlation-id" };

        await foreach (
            var item in interceptor
                .HandleAsync(query, (_, _) => GenerateItems(["a"]), cancellationToken)
                .ConfigureAwait(false)
        )
        {
            // Consume the stream
        }

        _ = await Assert.That(logger.Entries[0].Message).Contains("my-correlation-id");
    }

    [Test]
    public async Task HandleAsync_InvokesHandlerWithCorrectQuery(CancellationToken cancellationToken)
    {
        var logger = Mock.Logger<LoggingStreamQueryInterceptor<TestStreamQuery, string>>();
        var interceptor = CreateInterceptor(logger);
        var query = new TestStreamQuery();
        TestStreamQuery? received = null;

        await foreach (
            var item in interceptor
                .HandleAsync(
                    query,
                    (q, _) =>
                    {
                        received = q;
                        return GenerateItems(["a"]);
                    },
                    cancellationToken
                )
                .ConfigureAwait(false)
        )
        {
            // Consume the stream
        }

        _ = await Assert.That(received).IsSameReferenceAs(query);
    }

    [Test]
    public async Task HandleAsync_WithEmptyStream_LogsBeginAndEnd(CancellationToken cancellationToken)
    {
        var logger = Mock.Logger<LoggingStreamQueryInterceptor<TestStreamQuery, string>>();
        var interceptor = CreateInterceptor(logger);
        var query = new TestStreamQuery();

        var items = new List<string>();
        await foreach (
            var item in interceptor
                .HandleAsync(query, (_, _) => GenerateItems<string>([]), cancellationToken)
                .ConfigureAwait(false)
        )
        {
            items.Add(item);
        }

        using (Assert.Multiple())
        {
            _ = await Assert.That(items).IsEmpty();
            _ = await Assert.That(logger.Entries.Count).IsGreaterThanOrEqualTo(2);
            _ = await Assert.That(logger.Entries[0].Message).Contains("Streaming");
            _ = await Assert.That(logger.Entries[1].Message).Contains("Streamed");
        }
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
        int delayMs,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        foreach (var item in items)
        {
            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
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

    private static async IAsyncEnumerable<T> GenerateItemsThenThrow<T>(IEnumerable<T> items, Exception exception)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
        throw exception;
    }

    private sealed class TestStreamQuery : IStreamQuery<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }
}
