namespace NetEvolve.Pulse.Tests.Unit.Dispatchers;

using System.Collections.Concurrent;
using System.Threading.Tasks;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Dispatchers;
using NetEvolve.Pulse.Extensibility;
using TUnit.Core;

/// <summary>
/// Unit tests for <see cref="RateLimitedEventDispatcher"/>.
/// </summary>
public class RateLimitedEventDispatcherTests
{
    [Test]
    public async Task Constructor_WithDefaultConcurrency_CreatesWith5()
    {
        using var dispatcher = new RateLimitedEventDispatcher();

        _ = await Assert.That(dispatcher.MaxConcurrency).IsEqualTo(5);
    }

    [Test]
    public async Task Constructor_WithCustomConcurrency_CreatesWithSpecifiedValue()
    {
        using var dispatcher = new RateLimitedEventDispatcher(maxConcurrency: 10);

        _ = await Assert.That(dispatcher.MaxConcurrency).IsEqualTo(10);
    }

    [Test]
    public async Task Constructor_WithZeroConcurrency_ThrowsArgumentOutOfRangeException() =>
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            using var d = new RateLimitedEventDispatcher(maxConcurrency: 0);
        });

    [Test]
    public async Task Constructor_WithNegativeConcurrency_ThrowsArgumentOutOfRangeException() =>
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            using var d = new RateLimitedEventDispatcher(maxConcurrency: -1);
        });

    [Test]
    public async Task DispatchAsync_WithHandlers_InvokesAllHandlers()
    {
        using var dispatcher = new RateLimitedEventDispatcher(maxConcurrency: 2);
        var message = new TestEvent();
        var invokedHandlers = new ConcurrentBag<int>();
        var handlers = new List<IEventHandler<TestEvent>>
        {
            new TestEventHandler(1, invokedHandlers),
            new TestEventHandler(2, invokedHandlers),
            new TestEventHandler(3, invokedHandlers),
        };

        await dispatcher
            .DispatchAsync(
                message,
                handlers,
                (handler, msg) => handler.HandleAsync(msg, CancellationToken.None),
                CancellationToken.None
            )
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(invokedHandlers).Count().IsEqualTo(3);
            _ = await Assert.That(invokedHandlers).Contains(1);
            _ = await Assert.That(invokedHandlers).Contains(2);
            _ = await Assert.That(invokedHandlers).Contains(3);
        }
    }

    [Test]
    public async Task DispatchAsync_LimitsConcurrency()
    {
        using var dispatcher = new RateLimitedEventDispatcher(maxConcurrency: 2);
        var message = new TestEvent();
        var maxConcurrent = 0;
        var currentConcurrent = 0;
        var lockObj = new object();

        var handlers = Enumerable
            .Range(1, 10)
            .Select(_ => new ConcurrencyTrackingHandler(
                () =>
                {
                    lock (lockObj)
                    {
                        currentConcurrent++;
                        if (currentConcurrent > maxConcurrent)
                        {
                            maxConcurrent = currentConcurrent;
                        }
                    }
                },
                () =>
                {
                    lock (lockObj)
                    {
                        currentConcurrent--;
                    }
                }
            ))
            .Cast<IEventHandler<TestEvent>>()
            .ToList();

        await dispatcher
            .DispatchAsync(
                message,
                handlers,
                async (handler, msg) => await handler.HandleAsync(msg, CancellationToken.None).ConfigureAwait(false),
                CancellationToken.None
            )
            .ConfigureAwait(false);

        _ = await Assert.That(maxConcurrent).IsLessThanOrEqualTo(2);
    }

    [Test]
    public async Task DispatchAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var dispatcher = new RateLimitedEventDispatcher();
        var message = new TestEvent();
        var handlers = new List<IEventHandler<TestEvent>> { new TestEventHandler(1, []) };

        dispatcher.Dispose();

        _ = await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await dispatcher
                .DispatchAsync(
                    message,
                    handlers,
                    (handler, msg) => handler.HandleAsync(msg, CancellationToken.None),
                    CancellationToken.None
                )
                .ConfigureAwait(false)
        );
    }

    [Test]
    public async Task Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        var dispatcher = new RateLimitedEventDispatcher();

        dispatcher.Dispose();
        dispatcher.Dispose();

        await Task.CompletedTask.ConfigureAwait(false);
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
        private readonly ConcurrentBag<int> _invokedHandlers;

        public TestEventHandler(int id, ConcurrentBag<int> invokedHandlers)
        {
            _id = id;
            _invokedHandlers = invokedHandlers;
        }

        public Task HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
        {
            _invokedHandlers.Add(_id);
            return Task.CompletedTask;
        }
    }

    private sealed class ConcurrencyTrackingHandler : IEventHandler<TestEvent>
    {
        private readonly Action _onStart;
        private readonly Action _onEnd;

        public ConcurrencyTrackingHandler(Action onStart, Action onEnd)
        {
            _onStart = onStart;
            _onEnd = onEnd;
        }

        public async Task HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
        {
            _onStart();
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            _onEnd();
        }
    }
}
