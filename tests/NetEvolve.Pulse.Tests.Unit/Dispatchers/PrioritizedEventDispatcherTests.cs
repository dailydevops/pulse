namespace NetEvolve.Pulse.Tests.Unit.Dispatchers;

using System.Collections.Concurrent;
using System.Threading.Tasks;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Dispatchers;
using NetEvolve.Pulse.Extensibility;
using TUnit.Core;

/// <summary>
/// Unit tests for <see cref="PrioritizedEventDispatcher"/>.
/// </summary>
public class PrioritizedEventDispatcherTests
{
    [Test]
    public async Task DispatchAsync_WithPrioritizedHandlers_ExecutesInPriorityOrder()
    {
        var dispatcher = new PrioritizedEventDispatcher();
        var message = new TestEvent();
        var executionOrder = new ConcurrentQueue<int>();
        var handlers = new List<IEventHandler<TestEvent>>
        {
            new PrioritizedTestHandler(3, 100, executionOrder), // Low priority
            new PrioritizedTestHandler(1, 0, executionOrder), // Highest priority
            new PrioritizedTestHandler(2, 50, executionOrder), // Medium priority
        };

        await dispatcher.DispatchAsync(
            message,
            handlers,
            (handler, msg) => handler.HandleAsync(msg, CancellationToken.None),
            CancellationToken.None
        );

        var order = executionOrder.ToArray();
        using (Assert.Multiple())
        {
            _ = await Assert.That(order).Count().IsEqualTo(3);
            _ = await Assert.That(order[0]).IsEqualTo(1); // Priority 0 first
            _ = await Assert.That(order[1]).IsEqualTo(2); // Priority 50 second
            _ = await Assert.That(order[2]).IsEqualTo(3); // Priority 100 last
        }
    }

    [Test]
    public async Task DispatchAsync_WithNonPrioritizedHandlers_ExecutesLast()
    {
        var dispatcher = new PrioritizedEventDispatcher();
        var message = new TestEvent();
        var executionOrder = new ConcurrentQueue<int>();
        var handlers = new List<IEventHandler<TestEvent>>
        {
            new NonPrioritizedTestHandler(3, executionOrder), // No priority (int.MaxValue)
            new PrioritizedTestHandler(1, 0, executionOrder), // Highest priority
            new NonPrioritizedTestHandler(4, executionOrder), // No priority (int.MaxValue)
            new PrioritizedTestHandler(2, 500, executionOrder), // Medium priority
        };

        await dispatcher.DispatchAsync(
            message,
            handlers,
            (handler, msg) => handler.HandleAsync(msg, CancellationToken.None),
            CancellationToken.None
        );

        var order = executionOrder.ToArray();
        using (Assert.Multiple())
        {
            _ = await Assert.That(order).Count().IsEqualTo(4);
            _ = await Assert.That(order[0]).IsEqualTo(1); // Priority 0 first
            _ = await Assert.That(order[1]).IsEqualTo(2); // Priority 500 second
            // Non-prioritized handlers (3, 4) execute last, in registration order
            _ = await Assert.That(order[2]).IsEqualTo(3);
            _ = await Assert.That(order[3]).IsEqualTo(4);
        }
    }

    [Test]
    public async Task DispatchAsync_WithEqualPriority_PreservesRegistrationOrder()
    {
        var dispatcher = new PrioritizedEventDispatcher();
        var message = new TestEvent();
        var executionOrder = new ConcurrentQueue<int>();
        var handlers = new List<IEventHandler<TestEvent>>
        {
            new PrioritizedTestHandler(1, 100, executionOrder),
            new PrioritizedTestHandler(2, 100, executionOrder),
            new PrioritizedTestHandler(3, 100, executionOrder),
        };

        await dispatcher.DispatchAsync(
            message,
            handlers,
            (handler, msg) => handler.HandleAsync(msg, CancellationToken.None),
            CancellationToken.None
        );

        var order = executionOrder.ToArray();
        using (Assert.Multiple())
        {
            _ = await Assert.That(order).Count().IsEqualTo(3);
            _ = await Assert.That(order[0]).IsEqualTo(1);
            _ = await Assert.That(order[1]).IsEqualTo(2);
            _ = await Assert.That(order[2]).IsEqualTo(3);
        }
    }

    [Test]
    public async Task DispatchAsync_WithCancellation_StopsExecution()
    {
        var dispatcher = new PrioritizedEventDispatcher();
        var message = new TestEvent();
        var executionOrder = new ConcurrentQueue<int>();
        using var cts = new CancellationTokenSource();
        var handlers = new List<IEventHandler<TestEvent>>
        {
            new PrioritizedTestHandler(1, 0, executionOrder),
            new CancellingTestHandler(2, executionOrder, cts),
            new PrioritizedTestHandler(3, 100, executionOrder),
        };

        _ = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await dispatcher.DispatchAsync(
                message,
                handlers,
                (handler, msg) => handler.HandleAsync(msg, CancellationToken.None),
                cts.Token
            )
        );

        var order = executionOrder.ToArray();
        using (Assert.Multiple())
        {
            _ = await Assert.That(order).Count().IsEqualTo(2);
            _ = await Assert.That(order).Contains(1);
            _ = await Assert.That(order).Contains(2);
            _ = await Assert.That(order).DoesNotContain(3); // Should not execute after cancellation
        }
    }

    [Test]
    public async Task DispatchAsync_WithEmptyHandlers_CompletesSuccessfully()
    {
        var dispatcher = new PrioritizedEventDispatcher();
        var message = new TestEvent();
        var handlers = Enumerable.Empty<IEventHandler<TestEvent>>();

        await dispatcher.DispatchAsync(
            message,
            handlers,
            (handler, msg) => handler.HandleAsync(msg, CancellationToken.None),
            CancellationToken.None
        );

        await Task.CompletedTask;
    }

    private sealed class TestEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class PrioritizedTestHandler : IPrioritizedEventHandler<TestEvent>
    {
        private readonly int _id;
        private readonly ConcurrentQueue<int> _executionOrder;

        public PrioritizedTestHandler(int id, int priority, ConcurrentQueue<int> executionOrder)
        {
            _id = id;
            Priority = priority;
            _executionOrder = executionOrder;
        }

        public int Priority { get; }

        public Task HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
        {
            _executionOrder.Enqueue(_id);
            return Task.CompletedTask;
        }
    }

    private sealed class NonPrioritizedTestHandler : IEventHandler<TestEvent>
    {
        private readonly int _id;
        private readonly ConcurrentQueue<int> _executionOrder;

        public NonPrioritizedTestHandler(int id, ConcurrentQueue<int> executionOrder)
        {
            _id = id;
            _executionOrder = executionOrder;
        }

        public Task HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
        {
            _executionOrder.Enqueue(_id);
            return Task.CompletedTask;
        }
    }

    private sealed class CancellingTestHandler : IPrioritizedEventHandler<TestEvent>
    {
        private readonly int _id;
        private readonly ConcurrentQueue<int> _executionOrder;
        private readonly CancellationTokenSource _cts;

        public CancellingTestHandler(int id, ConcurrentQueue<int> executionOrder, CancellationTokenSource cts)
        {
            _id = id;
            _executionOrder = executionOrder;
            _cts = cts;
        }

        public int Priority => 50;

        public async Task HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
        {
            _executionOrder.Enqueue(_id);
            await _cts.CancelAsync();
        }
    }
}
