namespace NetEvolve.Pulse.Tests.Unit.Dispatchers;

using System.Threading.Tasks;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Dispatchers;
using NetEvolve.Pulse.Extensibility;
using TUnit.Core;

/// <summary>
/// Unit tests for <see cref="TransactionalEventDispatcher"/>.
/// </summary>
public class TransactionalEventDispatcherTests
{
    [Test]
    public async Task Constructor_WithNullOutbox_ThrowsArgumentNullException()
    {
        IEventOutbox? outbox = null;

        _ = Assert.Throws<ArgumentNullException>("outbox", () => _ = new TransactionalEventDispatcher(outbox!));
    }

    [Test]
    public async Task DispatchAsync_StoresEventInOutbox()
    {
        var outbox = new TestOutbox();
        var dispatcher = new TransactionalEventDispatcher(outbox);
        var message = new TestEvent { Id = "test-123" };
        var handlers = new List<IEventHandler<TestEvent>> { new TestEventHandler() };

        await dispatcher.DispatchAsync(
            message,
            handlers,
            (handler, msg) => handler.HandleAsync(msg, CancellationToken.None),
            CancellationToken.None
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(outbox.StoredEvents).Count().IsEqualTo(1);
            _ = await Assert.That(outbox.StoredEvents[0].Id).IsEqualTo("test-123");
        }
    }

    [Test]
    public async Task DispatchAsync_DoesNotInvokeHandlersDirectly()
    {
        var outbox = new TestOutbox();
        var dispatcher = new TransactionalEventDispatcher(outbox);
        var message = new TestEvent();
        var handlerInvoked = false;
        var handlers = new List<IEventHandler<TestEvent>> { new TrackingTestHandler(() => handlerInvoked = true) };

        await dispatcher.DispatchAsync(
            message,
            handlers,
            (handler, msg) =>
            {
                // This invoker should NOT be called by TransactionalEventDispatcher
                return handler.HandleAsync(msg, CancellationToken.None);
            },
            CancellationToken.None
        );

        _ = await Assert.That(handlerInvoked).IsFalse();
    }

    [Test]
    public async Task DispatchAsync_WithCancellation_PropagatesCancellation()
    {
        var outbox = new CancellationAwareOutbox();
        var dispatcher = new TransactionalEventDispatcher(outbox);
        var message = new TestEvent();
        var handlers = new List<IEventHandler<TestEvent>> { new TestEventHandler() };
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _ = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await dispatcher.DispatchAsync(
                message,
                handlers,
                (handler, msg) => handler.HandleAsync(msg, CancellationToken.None),
                cts.Token
            )
        );
    }

    [Test]
    public async Task DispatchAsync_WhenOutboxThrows_PropagatesException()
    {
        var outbox = new ThrowingOutbox();
        var dispatcher = new TransactionalEventDispatcher(outbox);
        var message = new TestEvent();
        var handlers = new List<IEventHandler<TestEvent>> { new TestEventHandler() };

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await dispatcher.DispatchAsync(
                message,
                handlers,
                (handler, msg) => handler.HandleAsync(msg, CancellationToken.None),
                CancellationToken.None
            )
        );
    }

    [Test]
    public async Task DispatchAsync_WithEmptyHandlers_StillStoresEvent()
    {
        var outbox = new TestOutbox();
        var dispatcher = new TransactionalEventDispatcher(outbox);
        var message = new TestEvent { Id = "empty-handlers-test" };
        var handlers = Enumerable.Empty<IEventHandler<TestEvent>>();

        await dispatcher.DispatchAsync(
            message,
            handlers,
            (handler, msg) => handler.HandleAsync(msg, CancellationToken.None),
            CancellationToken.None
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(outbox.StoredEvents).Count().IsEqualTo(1);
            _ = await Assert.That(outbox.StoredEvents[0].Id).IsEqualTo("empty-handlers-test");
        }
    }

    private sealed class TestEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class TestEventHandler : IEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TrackingTestHandler : IEventHandler<TestEvent>
    {
        private readonly Action _onHandle;

        public TrackingTestHandler(Action onHandle) => _onHandle = onHandle;

        public Task HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
        {
            _onHandle();
            return Task.CompletedTask;
        }
    }

    private sealed class TestOutbox : IEventOutbox
    {
        public List<IEvent> StoredEvents { get; } = [];

        public Task StoreAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default)
            where TEvent : IEvent
        {
            StoredEvents.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class CancellationAwareOutbox : IEventOutbox
    {
        public Task StoreAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default)
            where TEvent : IEvent
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingOutbox : IEventOutbox
    {
        public Task StoreAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default)
            where TEvent : IEvent => throw new InvalidOperationException("Outbox error");
    }
}
