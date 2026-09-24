namespace NetEvolve.Pulse.Tests.Unit.AspNetCore;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using TUnit.Assertions.Enums;
using TUnit.Core;
using TUnit.Mocks;

[TestGroup("AspNetCore")]
public sealed class PulseStreamHubTests
{
    [Test]
    public async Task Constructor_WithNullMediator_ThrowsArgumentNullException() =>
        _ = await Assert.That(() => new PulseStreamHub<TestStreamQuery, string>(null!)).Throws<ArgumentNullException>();

    [Test]
    public async Task StreamAsync_WithNullQuery_ThrowsArgumentNullException()
    {
        var mediator = Mock.Of<IMediator>();
        using var hub = new PulseStreamHub<TestStreamQuery, string>(mediator.Object);

        _ = await Assert.That(() => hub.StreamAsync(null!, CancellationToken.None)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task StreamAsync_WithItems_YieldsAllItemsInOrder(CancellationToken cancellationToken)
    {
        var query = new TestStreamQuery();
        var mediator = Mock.Of<IMediator>();
        _ = mediator
            .StreamQueryAsync<TestStreamQuery, string>(query, Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable("first", "second", "third"));
        using var hub = new PulseStreamHub<TestStreamQuery, string>(mediator.Object);

        var items = await CollectAsync(hub.StreamAsync(query, cancellationToken)).ConfigureAwait(false);

        _ = await Assert.That(items).IsEquivalentTo(["first", "second", "third"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task StreamAsync_WithEmptyStream_YieldsNothing(CancellationToken cancellationToken)
    {
        var query = new TestStreamQuery();
        var mediator = Mock.Of<IMediator>();
        _ = mediator
            .StreamQueryAsync<TestStreamQuery, string>(query, Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable());
        using var hub = new PulseStreamHub<TestStreamQuery, string>(mediator.Object);

        var items = await CollectAsync(hub.StreamAsync(query, cancellationToken)).ConfigureAwait(false);

        _ = await Assert.That(items).IsEmpty();
    }

    [Test]
    public async Task StreamAsync_ForwardsCancellationTokenToMediator()
    {
        using var cts = new CancellationTokenSource();
        var query = new TestStreamQuery();
        var mediator = Mock.Of<IMediator>();
        _ = mediator
            .StreamQueryAsync<TestStreamQuery, string>(query, Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable("first"));
        using var hub = new PulseStreamHub<TestStreamQuery, string>(mediator.Object);

        _ = await CollectAsync(hub.StreamAsync(query, cts.Token)).ConfigureAwait(false);

        mediator.StreamQueryAsync<TestStreamQuery, string>(query, cts.Token).WasCalled(Times.Once);
    }

    [Test]
    public async Task StreamAsync_WhenHandlerThrows_PropagatesException(CancellationToken cancellationToken)
    {
        var query = new TestStreamQuery();
        var mediator = Mock.Of<IMediator>();
        _ = mediator
            .StreamQueryAsync<TestStreamQuery, string>(query, Arg.Any<CancellationToken>())
            .Returns(ThrowingAsyncEnumerable(new InvalidOperationException("Handler failure.")));
        using var hub = new PulseStreamHub<TestStreamQuery, string>(mediator.Object);

        _ = await Assert
            .That(async () => await CollectAsync(hub.StreamAsync(query, cancellationToken)).ConfigureAwait(false))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task StreamAsync_WhenForeignCancellationThrown_PropagatesAsError(CancellationToken cancellationToken)
    {
        var query = new TestStreamQuery();
        var mediator = Mock.Of<IMediator>();
        _ = mediator
            .StreamQueryAsync<TestStreamQuery, string>(query, Arg.Any<CancellationToken>())
            .Returns(ThrowingAsyncEnumerable(new OperationCanceledException("Foreign cancellation.")));
        using var hub = new PulseStreamHub<TestStreamQuery, string>(mediator.Object);

        _ = await Assert
            .That(async () => await CollectAsync(hub.StreamAsync(query, cancellationToken)).ConfigureAwait(false))
            .Throws<OperationCanceledException>();
    }

    // INVARIANT: SignalR cancels the token passed to a streaming hub method when the client
    // unsubscribes; the hub must then end the stream without surfacing an exception.
    [Test]
    public async Task StreamAsync_WhenClientCancels_CompletesWithoutException()
    {
        using var cts = new CancellationTokenSource();
        var query = new TestStreamQuery();
        var mediator = Mock.Of<IMediator>();
        _ = mediator
            .StreamQueryAsync<TestStreamQuery, string>(query, Arg.Any<CancellationToken>())
            .Returns(InfiniteAsyncEnumerable(cts.Token));
        using var hub = new PulseStreamHub<TestStreamQuery, string>(mediator.Object);

        var received = new List<string>();
        await foreach (var item in hub.StreamAsync(query, cts.Token).ConfigureAwait(false))
        {
            received.Add(item);
            if (received.Count == 2)
            {
                await cts.CancelAsync().ConfigureAwait(false);
            }
        }

        _ = await Assert.That(received).IsEquivalentTo(["item-0", "item-1"], CollectionOrdering.Matching);
    }

    // INVARIANT: SignalR may also flow the unsubscribe signal through
    // GetAsyncEnumerator(token); that token must reach the real stream handler through
    // the mediator so the handler stops producing items.
    [Test]
    public async Task StreamAsync_WithRealMediator_EnumeratorCancellationReachesHandler()
    {
        var handler = new InfiniteStreamQueryHandler();
        var services = new ServiceCollection().AddLogging();
        _ = services.AddSingleton<IStreamQueryHandler<TestStreamQuery, string>>(handler);
        _ = services.AddPulse(_ => { });
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            using var hub = new PulseStreamHub<TestStreamQuery, string>(provider.GetRequiredService<IMediator>());
            using var cts = new CancellationTokenSource();

            var received = 0;
            await foreach (
                var _ in hub.StreamAsync(new TestStreamQuery(), CancellationToken.None)
                    .WithCancellation(cts.Token)
                    .ConfigureAwait(false)
            )
            {
                if (++received == 3)
                {
                    await cts.CancelAsync().ConfigureAwait(false);
                }
                else if (received > 100)
                {
                    // Guard: fail with an assertion instead of hanging if the token never reaches the handler.
                    break;
                }
            }

            _ = await Assert.That(received).IsEqualTo(3);
            _ = await Assert.That(handler.ObservedCancellation).IsTrue();
        }
    }

    private static async Task<List<string>> CollectAsync(IAsyncEnumerable<string> source)
    {
        var items = new List<string>();
        await foreach (var item in source.ConfigureAwait(false))
        {
            items.Add(item);
        }

        return items;
    }

#pragma warning disable CS1998 // Async method lacks 'await' operators
    private static async IAsyncEnumerable<string> ToAsyncEnumerable(params string[] items)
    {
        foreach (var item in items)
        {
            yield return item;
        }
    }

    private static async IAsyncEnumerable<string> ThrowingAsyncEnumerable(Exception exception)
    {
        yield return "before-failure";
        throw exception;
    }
#pragma warning restore CS1998

    private static async IAsyncEnumerable<string> InfiniteAsyncEnumerable(
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var counter = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            yield return $"item-{counter++}";
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        }
    }

    internal sealed record TestStreamQuery : IStreamQuery<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class InfiniteStreamQueryHandler : IStreamQueryHandler<TestStreamQuery, string>
    {
        public bool ObservedCancellation { get; private set; }

        public async IAsyncEnumerable<string> HandleAsync(
            TestStreamQuery request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            var counter = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                yield return $"item-{counter++}";
                try
                {
                    await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    ObservedCancellation = true;
                    throw;
                }
            }
        }
    }
}
