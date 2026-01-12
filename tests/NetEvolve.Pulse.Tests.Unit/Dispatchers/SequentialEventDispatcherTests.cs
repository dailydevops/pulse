namespace NetEvolve.Pulse.Tests.Unit.Dispatchers;

using NetEvolve.Pulse;
using NetEvolve.Pulse.Dispatchers;
using NetEvolve.Pulse.Extensibility;
using TUnit.Core;

public class SequentialEventDispatcherTests
{
    [Test]
    public async Task DispatchAsync_WithMultipleHandlers_InvokesAllHandlersInOrder()
    {
        var dispatcher = new SequentialEventDispatcher();
        var testEvent = new TestEvent();
        var executionOrder = new List<int>();
        var handlers = new List<IEventHandler<TestEvent>>
        {
            new TestEventHandler(1, executionOrder),
            new TestEventHandler(2, executionOrder),
            new TestEventHandler(3, executionOrder),
        };

        await dispatcher
            .DispatchAsync(
                testEvent,
                handlers,
                async (handler, evt) => await handler.HandleAsync(evt, CancellationToken.None).ConfigureAwait(false),
                CancellationToken.None
            )
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(executionOrder).Count().IsEqualTo(3);
            _ = await Assert.That(executionOrder[0]).IsEqualTo(1);
            _ = await Assert.That(executionOrder[1]).IsEqualTo(2);
            _ = await Assert.That(executionOrder[2]).IsEqualTo(3);
        }
    }

    [Test]
    public async Task DispatchAsync_WithNoHandlers_CompletesSuccessfully()
    {
        var dispatcher = new SequentialEventDispatcher();
        var testEvent = new TestEvent();
        var handlers = Array.Empty<IEventHandler<TestEvent>>();

        await dispatcher
            .DispatchAsync(
                testEvent,
                handlers,
                async (handler, evt) => await handler.HandleAsync(evt, CancellationToken.None).ConfigureAwait(false),
                CancellationToken.None
            )
            .ConfigureAwait(false);
    }

    [Test]
    public async Task DispatchAsync_WithSingleHandler_InvokesHandler()
    {
        var dispatcher = new SequentialEventDispatcher();
        var testEvent = new TestEvent();
        var executionOrder = new List<int>();
        var handlers = new List<IEventHandler<TestEvent>> { new TestEventHandler(1, executionOrder) };

        await dispatcher
            .DispatchAsync(
                testEvent,
                handlers,
                async (handler, evt) => await handler.HandleAsync(evt, CancellationToken.None).ConfigureAwait(false),
                CancellationToken.None
            )
            .ConfigureAwait(false);

        _ = await Assert.That(executionOrder).HasSingleItem();
    }

    [Test]
    public async Task DispatchAsync_WithCancellationBetweenHandlers_StopsExecution()
    {
        var dispatcher = new SequentialEventDispatcher();
        var testEvent = new TestEvent();
        var executionOrder = new List<int>();
        using var cts = new CancellationTokenSource();
        var handlers = new List<IEventHandler<TestEvent>>
        {
            new CancellingEventHandler(1, executionOrder, cts),
            new TestEventHandler(2, executionOrder),
            new TestEventHandler(3, executionOrder),
        };

        _ = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await dispatcher
                .DispatchAsync(
                    testEvent,
                    handlers,
                    async (handler, evt) =>
                        await handler.HandleAsync(evt, CancellationToken.None).ConfigureAwait(false),
                    cts.Token
                )
                .ConfigureAwait(false)
        );

        _ = await Assert.That(executionOrder).HasSingleItem();
    }

    [Test]
    public async Task DispatchAsync_ExecutesSequentially_NotInParallel()
    {
        var dispatcher = new SequentialEventDispatcher();
        var testEvent = new TestEvent();
        var concurrentCount = 0;
        var maxConcurrent = 0;
        var executionOrder = new List<int>();
        var handlers = new List<IEventHandler<TestEvent>>
        {
            new ConcurrencyTrackingHandler(
                1,
                executionOrder,
                () => concurrentCount,
                c =>
                {
                    concurrentCount = c;
                    maxConcurrent = Math.Max(maxConcurrent, c);
                }
            ),
            new ConcurrencyTrackingHandler(
                2,
                executionOrder,
                () => concurrentCount,
                c =>
                {
                    concurrentCount = c;
                    maxConcurrent = Math.Max(maxConcurrent, c);
                }
            ),
            new ConcurrencyTrackingHandler(
                3,
                executionOrder,
                () => concurrentCount,
                c =>
                {
                    concurrentCount = c;
                    maxConcurrent = Math.Max(maxConcurrent, c);
                }
            ),
        };

        await dispatcher
            .DispatchAsync(
                testEvent,
                handlers,
                async (handler, evt) => await handler.HandleAsync(evt, CancellationToken.None).ConfigureAwait(false),
                CancellationToken.None
            )
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(maxConcurrent).IsEqualTo(1);
            _ = await Assert.That(executionOrder).Count().IsEqualTo(3);
        }
    }

    private sealed class TestEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class TestEventHandler : IEventHandler<TestEvent>
    {
        private readonly int _id;
        private readonly List<int> _executionOrder;

        public TestEventHandler(int id, List<int> executionOrder)
        {
            _id = id;
            _executionOrder = executionOrder;
        }

        public Task HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
        {
            _executionOrder.Add(_id);
            return Task.CompletedTask;
        }
    }

    private sealed class CancellingEventHandler : IEventHandler<TestEvent>
    {
        private readonly int _id;
        private readonly List<int> _executionOrder;
        private readonly CancellationTokenSource _cts;

        public CancellingEventHandler(int id, List<int> executionOrder, CancellationTokenSource cts)
        {
            _id = id;
            _executionOrder = executionOrder;
            _cts = cts;
        }

        public async Task HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
        {
            _executionOrder.Add(_id);
            await _cts.CancelAsync().ConfigureAwait(false);
        }
    }

    private sealed class ConcurrencyTrackingHandler : IEventHandler<TestEvent>
    {
        private readonly int _id;
        private readonly List<int> _executionOrder;
        private readonly Func<int> _getConcurrent;
        private readonly Action<int> _setConcurrent;

        public ConcurrencyTrackingHandler(
            int id,
            List<int> executionOrder,
            Func<int> getConcurrent,
            Action<int> setConcurrent
        )
        {
            _id = id;
            _executionOrder = executionOrder;
            _getConcurrent = getConcurrent;
            _setConcurrent = setConcurrent;
        }

        public async Task HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
        {
            _setConcurrent(_getConcurrent() + 1);
            _executionOrder.Add(_id);
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            _setConcurrent(_getConcurrent() - 1);
        }
    }
}
