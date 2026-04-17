namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System.Diagnostics;
using System.Runtime.CompilerServices;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using TUnit.Core;

[TestGroup("Interceptors")]
public class ActivityAndMetricsStreamQueryInterceptorTests
{
    [Test]
    [NotInParallel]
    public async Task HandleAsync_CreatesActivityWithCorrectTags(CancellationToken cancellationToken)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "NetEvolve.Pulse", StringComparison.Ordinal),
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var timeProvider = TimeProvider.System;
        var interceptor = new ActivityAndMetricsStreamQueryInterceptor<TestStreamQuery, int>(timeProvider);
        var query = new TestStreamQuery();
        Activity? capturedActivity = null;

        listener.ActivityStarted = activity => capturedActivity = activity;

        await foreach (
            var _ in interceptor
                .HandleAsync(query, (_, ct) => Items([1, 2, 3], ct), cancellationToken)
                .ConfigureAwait(false)
        )
        {
            // consume items
        }

        using (Assert.Multiple())
        {
            _ = await Assert.That(capturedActivity).IsNotNull();
            _ = await Assert.That(capturedActivity!.DisplayName).IsEqualTo("StreamQuery.TestStreamQuery");
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.request.type")).IsEqualTo("StreamQuery");
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.request.name")).IsEqualTo("TestStreamQuery");
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.response.type")).IsEqualTo("Int32");
        }
    }

    [Test]
    [NotInParallel]
    public async Task HandleAsync_WhenStreamCompletes_SetsActivityStatusToOk(CancellationToken cancellationToken)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "NetEvolve.Pulse", StringComparison.Ordinal),
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var timeProvider = TimeProvider.System;
        var interceptor = new ActivityAndMetricsStreamQueryInterceptor<TestStreamQuery, int>(timeProvider);
        var query = new TestStreamQuery();
        Activity? capturedActivity = null;

        listener.ActivityStopped = activity => capturedActivity = activity;

        await foreach (
            var _ in interceptor
                .HandleAsync(query, (_, ct) => Items([1, 2, 3], ct), cancellationToken)
                .ConfigureAwait(false)
        )
        {
            // consume items
        }

        using (Assert.Multiple())
        {
            _ = await Assert.That(capturedActivity).IsNotNull();
            _ = await Assert.That(capturedActivity!.Status).IsEqualTo(ActivityStatusCode.Ok);
#pragma warning disable CS8605 // Unboxing a possibly null value.
            _ = await Assert.That((bool)capturedActivity.GetTagItem("pulse.success")).IsTrue();
#pragma warning restore CS8605 // Unboxing a possibly null value.
        }
    }

    [Test]
    [NotInParallel]
    public async Task HandleAsync_WhenHandlerThrows_SetsActivityStatusToError(CancellationToken cancellationToken)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "NetEvolve.Pulse", StringComparison.Ordinal),
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var timeProvider = TimeProvider.System;
        var interceptor = new ActivityAndMetricsStreamQueryInterceptor<TestStreamQuery, int>(timeProvider);
        var query = new TestStreamQuery();
        var testException = new InvalidOperationException("Test exception");
        Activity? capturedActivity = null;

        listener.ActivityStopped = activity => capturedActivity = activity;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (
                var _ in interceptor
                    .HandleAsync(query, (_, ct) => ThrowingItems(testException, ct), cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                // consume items until exception
            }
        });

        using (Assert.Multiple())
        {
            _ = await Assert.That(exception).IsSameReferenceAs(testException);
            _ = await Assert.That(capturedActivity).IsNotNull();
            _ = await Assert.That(capturedActivity!.Status).IsEqualTo(ActivityStatusCode.Error);
            _ = await Assert.That(capturedActivity.StatusDescription).IsEqualTo("Test exception");
#pragma warning disable CS8605 // Unboxing a possibly null value.
            _ = await Assert.That((bool)capturedActivity.GetTagItem("pulse.success")).IsFalse();
#pragma warning restore CS8605 // Unboxing a possibly null value.
            _ = await Assert
                .That(capturedActivity.GetTagItem("pulse.exception.type"))
                .IsEqualTo("System.InvalidOperationException");
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.exception.message")).IsEqualTo("Test exception");
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.exception.stacktrace")).IsNotNull();
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.exception.timestamp")).IsNotNull();
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.response.timestamp")).IsNull();
        }
    }

    [Test]
    [NotInParallel]
    public async Task HandleAsync_WithEmptyStream_SetsActivityStatusToOk(CancellationToken cancellationToken)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "NetEvolve.Pulse", StringComparison.Ordinal),
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var timeProvider = TimeProvider.System;
        var interceptor = new ActivityAndMetricsStreamQueryInterceptor<TestStreamQuery, int>(timeProvider);
        var query = new TestStreamQuery();
        Activity? capturedActivity = null;

        listener.ActivityStopped = activity => capturedActivity = activity;

        await foreach (
            var _ in interceptor
                .HandleAsync(query, (_, ct) => Items(Array.Empty<int>(), ct), cancellationToken)
                .ConfigureAwait(false)
        )
        {
            // empty stream
        }

        using (Assert.Multiple())
        {
            _ = await Assert.That(capturedActivity).IsNotNull();
            _ = await Assert.That(capturedActivity!.Status).IsEqualTo(ActivityStatusCode.Ok);
#pragma warning disable CS8605 // Unboxing a possibly null value.
            _ = await Assert.That((bool)capturedActivity.GetTagItem("pulse.success")).IsTrue();
#pragma warning restore CS8605 // Unboxing a possibly null value.
        }
    }

    [Test]
    public async Task HandleAsync_YieldsAllItemsUnchanged(CancellationToken cancellationToken)
    {
        var timeProvider = TimeProvider.System;
        var interceptor = new ActivityAndMetricsStreamQueryInterceptor<TestStreamQuery, int>(timeProvider);
        var query = new TestStreamQuery();
        var expected = new[] { 10, 20, 30 };
        var received = new List<int>();

        await foreach (
            var item in interceptor
                .HandleAsync(query, (_, ct) => Items(expected, ct), cancellationToken)
                .ConfigureAwait(false)
        )
        {
            received.Add(item);
        }

        _ = await Assert.That(received).IsEquivalentTo(expected);
    }

    [Test]
    [NotInParallel]
    public async Task HandleAsync_SetsTimestamps(CancellationToken cancellationToken)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "NetEvolve.Pulse", StringComparison.Ordinal),
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var timeProvider = TimeProvider.System;
        var interceptor = new ActivityAndMetricsStreamQueryInterceptor<TestStreamQuery, int>(timeProvider);
        var query = new TestStreamQuery();
        Activity? capturedActivity = null;

        listener.ActivityStopped = activity => capturedActivity = activity;

        await foreach (
            var _ in interceptor.HandleAsync(query, (_, ct) => Items([1], ct), cancellationToken).ConfigureAwait(false)
        )
        {
            // consume
        }

        using (Assert.Multiple())
        {
            _ = await Assert.That(capturedActivity).IsNotNull();
            _ = await Assert.That(capturedActivity!.GetTagItem("pulse.request.timestamp")).IsNotNull();
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.response.timestamp")).IsNotNull();
        }
    }

    [Test]
    public async Task HandleAsync_InvokesHandlerWithCorrectQuery(CancellationToken cancellationToken)
    {
        var timeProvider = TimeProvider.System;
        var interceptor = new ActivityAndMetricsStreamQueryInterceptor<TestStreamQuery, int>(timeProvider);
        var query = new TestStreamQuery();
        TestStreamQuery? receivedQuery = null;

        await foreach (
            var _ in interceptor
                .HandleAsync(
                    query,
                    (q, ct) =>
                    {
                        receivedQuery = q;
                        return Items([1], ct);
                    },
                    cancellationToken
                )
                .ConfigureAwait(false)
        )
        {
            // consume
        }

        _ = await Assert.That(receivedQuery).IsSameReferenceAs(query);
    }

    [Test]
    [NotInParallel]
    public async Task HandleAsync_WithNullCausationId_DoesNotTagCausationId(CancellationToken cancellationToken)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "NetEvolve.Pulse", StringComparison.Ordinal),
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var timeProvider = TimeProvider.System;
        var interceptor = new ActivityAndMetricsStreamQueryInterceptor<TestStreamQuery, int>(timeProvider);
        var query = new TestStreamQuery { CausationId = null };
        Activity? capturedActivity = null;

        listener.ActivityStarted = activity => capturedActivity = activity;

        await foreach (
            var _ in interceptor
                .HandleAsync(query, (_, ct) => Items([1, 2, 3], ct), cancellationToken)
                .ConfigureAwait(false)
        )
        {
            // consume items
        }

        using (Assert.Multiple())
        {
            _ = await Assert.That(capturedActivity).IsNotNull();
            _ = await Assert.That(capturedActivity!.GetTagItem("pulse.causation_id")).IsNull();
        }
    }

    [Test]
    [NotInParallel]
    public async Task HandleAsync_WithNonNullCausationId_TagsCausationId(CancellationToken cancellationToken)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "NetEvolve.Pulse", StringComparison.Ordinal),
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var timeProvider = TimeProvider.System;
        var interceptor = new ActivityAndMetricsStreamQueryInterceptor<TestStreamQuery, int>(timeProvider);
        var query = new TestStreamQuery { CausationId = "evt-1" };
        Activity? capturedActivity = null;

        listener.ActivityStarted = activity => capturedActivity = activity;

        await foreach (
            var _ in interceptor
                .HandleAsync(query, (_, ct) => Items([1, 2, 3], ct), cancellationToken)
                .ConfigureAwait(false)
        )
        {
            // consume items
        }

        using (Assert.Multiple())
        {
            _ = await Assert.That(capturedActivity).IsNotNull();
            _ = await Assert.That(capturedActivity!.GetTagItem("pulse.causation_id")).IsEqualTo("evt-1");
        }
    }

    private static IAsyncEnumerable<T> Items<T>(IEnumerable<T> items, CancellationToken cancellationToken = default) =>
        ItemsCore(items, cancellationToken);

    private static async IAsyncEnumerable<T> ItemsCore<T>(
        IEnumerable<T> items,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }
    }

    private static async IAsyncEnumerable<int> ThrowingItems(
        Exception exception,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        yield return 1;
        cancellationToken.ThrowIfCancellationRequested();
        throw exception;
    }

    private sealed class TestStreamQuery : IStreamQuery<int>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }
}
