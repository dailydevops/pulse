namespace NetEvolve.Pulse.Tests.Unit.AspNetCore.Grpc;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using global::Grpc.Core;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using TUnit.Core;
using TUnit.Mocks;

[TestGroup("AspNetCore")]
public sealed class PulseGrpcStreamServiceTests
{
    [Test]
    public void Constructor_WithNullMediator_ThrowsArgumentNullException() =>
        _ = Assert.Throws<ArgumentNullException>(() => _ = new TestStreamService(null!));

    [Test]
    public async Task StreamAsync_WithNullQuery_ThrowsArgumentNullException()
    {
        var service = new TestStreamService(Mock.Of<IMediator>().Object);

        _ = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.Stream(null!, new CollectingStreamWriter(), new TestServerCallContext(CancellationToken.None))
        );
    }

    [Test]
    public async Task StreamAsync_WithNullResponseStream_ThrowsArgumentNullException()
    {
        var service = new TestStreamService(Mock.Of<IMediator>().Object);

        _ = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.Stream(new TestStreamQuery(), null!, new TestServerCallContext(CancellationToken.None))
        );
    }

    [Test]
    public async Task StreamAsync_WithNullContext_ThrowsArgumentNullException()
    {
        var service = new TestStreamService(Mock.Of<IMediator>().Object);

        _ = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.Stream(new TestStreamQuery(), new CollectingStreamWriter(), null!)
        );
    }

    [Test]
    public async Task StreamAsync_WithItems_WritesAllItemsInOrder()
    {
        var mediator = Mock.Of<IMediator>();
        _ = mediator
            .StreamQueryAsync<TestStreamQuery, string>(Arg.Any<TestStreamQuery>(), Arg.Any<CancellationToken>())
            .Returns(() => YieldAsync(["first", "second", "third"]));
        var writer = new CollectingStreamWriter();

        await new TestStreamService(mediator.Object)
            .Stream(new TestStreamQuery(), writer, new TestServerCallContext(CancellationToken.None))
            .ConfigureAwait(false);

        _ = await Assert.That(writer.Items).IsEquivalentTo(["first", "second", "third"]);
    }

    [Test]
    public async Task StreamAsync_WithEmptyStream_WritesNothing()
    {
        var mediator = Mock.Of<IMediator>();
        _ = mediator
            .StreamQueryAsync<TestStreamQuery, string>(Arg.Any<TestStreamQuery>(), Arg.Any<CancellationToken>())
            .Returns(() => YieldAsync([]));
        var writer = new CollectingStreamWriter();

        await new TestStreamService(mediator.Object)
            .Stream(new TestStreamQuery(), writer, new TestServerCallContext(CancellationToken.None))
            .ConfigureAwait(false);

        _ = await Assert.That(writer.Items).IsEmpty();
    }

    [Test]
    public async Task StreamAsync_ForwardsQueryAndContextCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var query = new TestStreamQuery();
        var mediator = Mock.Of<IMediator>();
        _ = mediator
            .StreamQueryAsync<TestStreamQuery, string>(Arg.Any<TestStreamQuery>(), Arg.Any<CancellationToken>())
            .Returns(() => YieldAsync(["item"]));

        await new TestStreamService(mediator.Object)
            .Stream(query, new CollectingStreamWriter(), new TestServerCallContext(cts.Token))
            .ConfigureAwait(false);

        mediator.StreamQueryAsync<TestStreamQuery, string>(query, cts.Token).WasCalled(Times.Once);
    }

    [Test]
    public async Task StreamAsync_WhenCancelledDuringStream_StopsWritingEvenIfHandlerIgnoresToken()
    {
        using var cts = new CancellationTokenSource();
        var mediator = Mock.Of<IMediator>();
        _ = mediator
            .StreamQueryAsync<TestStreamQuery, string>(Arg.Any<TestStreamQuery>(), Arg.Any<CancellationToken>())
            .Returns(() => YieldAsync(["first", "second", "third"]));
        var writer = new CollectingStreamWriter(onWrite: () => cts.Cancel());

        _ = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new TestStreamService(mediator.Object).Stream(
                new TestStreamQuery(),
                writer,
                new TestServerCallContext(cts.Token)
            )
        );

        _ = await Assert.That(writer.Items).IsEquivalentTo(["first"]);
    }

    [Test]
    public async Task StreamAsync_WhenAlreadyCancelled_WritesNothing()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(false);
        var mediator = Mock.Of<IMediator>();
        _ = mediator
            .StreamQueryAsync<TestStreamQuery, string>(Arg.Any<TestStreamQuery>(), Arg.Any<CancellationToken>())
            .Returns(() => YieldAsync(["first"]));
        var writer = new CollectingStreamWriter();

        _ = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new TestStreamService(mediator.Object).Stream(
                new TestStreamQuery(),
                writer,
                new TestServerCallContext(cts.Token)
            )
        );

        _ = await Assert.That(writer.Items).IsEmpty();
    }

    [Test]
    public async Task StreamAsync_WithCancellableTokenAndWriterWithoutTokenOverload_WritesAllItems()
    {
        using var cts = new CancellationTokenSource();
        var mediator = Mock.Of<IMediator>();
        _ = mediator
            .StreamQueryAsync<TestStreamQuery, string>(Arg.Any<TestStreamQuery>(), Arg.Any<CancellationToken>())
            .Returns(() => YieldAsync(["first", "second"]));
        var writer = new CollectingStreamWriter();

        await new TestStreamService(mediator.Object)
            .Stream(new TestStreamQuery(), writer, new TestServerCallContext(cts.Token))
            .ConfigureAwait(false);

        _ = await Assert.That(writer.Items).IsEquivalentTo(["first", "second"]);
    }

    [Test]
    public async Task StreamAsync_WhenHandlerThrows_PropagatesExceptionAfterWrittenItems()
    {
        var mediator = Mock.Of<IMediator>();
        _ = mediator
            .StreamQueryAsync<TestStreamQuery, string>(Arg.Any<TestStreamQuery>(), Arg.Any<CancellationToken>())
            .Returns(() => YieldThenThrowAsync("first", new InvalidOperationException("boom")));
        var writer = new CollectingStreamWriter();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new TestStreamService(mediator.Object).Stream(
                new TestStreamQuery(),
                writer,
                new TestServerCallContext(CancellationToken.None)
            )
        );

        _ = await Assert.That(exception!.Message).IsEqualTo("boom");
        _ = await Assert.That(writer.Items).IsEquivalentTo(["first"]);
    }

    [Test]
    public async Task StreamAsync_WhenWriterThrows_PropagatesException()
    {
        var mediator = Mock.Of<IMediator>();
        _ = mediator
            .StreamQueryAsync<TestStreamQuery, string>(Arg.Any<TestStreamQuery>(), Arg.Any<CancellationToken>())
            .Returns(() => YieldAsync(["first"]));
        var writer = new CollectingStreamWriter(onWrite: () =>
            throw new RpcException(new Status(StatusCode.Unavailable, "gone"))
        );

        var exception = await Assert.ThrowsAsync<RpcException>(() =>
            new TestStreamService(mediator.Object).Stream(
                new TestStreamQuery(),
                writer,
                new TestServerCallContext(CancellationToken.None)
            )
        );

        _ = await Assert.That(exception!.StatusCode).IsEqualTo(StatusCode.Unavailable);
    }

#pragma warning disable CS1998 // Async iterators without await are intentional to simulate handlers that ignore cancellation.
    private static async IAsyncEnumerable<string> YieldAsync(string[] items)
    {
        foreach (var item in items)
        {
            yield return item;
        }
    }

    private static async IAsyncEnumerable<string> YieldThenThrowAsync(string item, Exception exception)
    {
        yield return item;
        throw exception;
    }
#pragma warning restore CS1998
}
