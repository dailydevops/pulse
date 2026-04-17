namespace NetEvolve.Pulse.Tests.Unit.Dispatchers;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Dispatchers;
using NetEvolve.Pulse.Extensibility;
using TUnit.Core;

[TestGroup("Dispatchers")]
public class ParallelEventDispatcherTests
{
    [Test]
    public async Task DispatchAsync_WithMultipleHandlers_InvokesAllHandlers(CancellationToken cancellationToken)
    {
        var dispatcher = new ParallelEventDispatcher();
        var testEvent = new TestEvent();
        var invokedHandlers = new List<int>();
        var handlers = new List<IEventHandler<TestEvent>>
        {
            new TestEventHandler(1, invokedHandlers),
            new TestEventHandler(2, invokedHandlers),
            new TestEventHandler(3, invokedHandlers),
        };

        await dispatcher
            .DispatchAsync(
                testEvent,
                handlers,
                async (handler, evt, ct) => await handler.HandleAsync(evt, ct).ConfigureAwait(false),
                cancellationToken
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
    public async Task DispatchAsync_WithNoHandlers_CompletesSuccessfully(CancellationToken cancellationToken)
    {
        var dispatcher = new ParallelEventDispatcher();
        var testEvent = new TestEvent();
        var handlers = Array.Empty<IEventHandler<TestEvent>>();

        await dispatcher
            .DispatchAsync(
                testEvent,
                handlers,
                async (handler, evt, ct) => await handler.HandleAsync(evt, ct).ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    [Test]
    public async Task DispatchAsync_WithSingleHandler_InvokesHandler(CancellationToken cancellationToken)
    {
        var dispatcher = new ParallelEventDispatcher();
        var testEvent = new TestEvent();
        var invokedHandlers = new List<int>();
        var handlers = new List<IEventHandler<TestEvent>> { new TestEventHandler(1, invokedHandlers) };

        await dispatcher
            .DispatchAsync(
                testEvent,
                handlers,
                async (handler, evt, ct) => await handler.HandleAsync(evt, ct).ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(invokedHandlers).HasSingleItem();
    }

    [Test]
    public async Task DispatchAsync_WithCancellation_RespectsToken(CancellationToken cancellationToken)
    {
        var dispatcher = new ParallelEventDispatcher();
        var testEvent = new TestEvent();
        var invokedHandlers = new List<int>();
        var handlers = new List<IEventHandler<TestEvent>> { new TestEventHandler(1, invokedHandlers) };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await cts.CancelAsync().ConfigureAwait(false);

        _ = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await dispatcher
                .DispatchAsync(
                    testEvent,
                    handlers,
                    async (handler, evt, ct) => await handler.HandleAsync(evt, ct).ConfigureAwait(false),
                    cts.Token
                )
                .ConfigureAwait(false)
        );
    }

    private sealed class TestEvent : IEvent
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public string Id { get; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class TestEventHandler : IEventHandler<TestEvent>
    {
        private readonly int _id;
        private readonly List<int> _invokedHandlers;

        public TestEventHandler(int id, List<int> invokedHandlers)
        {
            _id = id;
            _invokedHandlers = invokedHandlers;
        }

        public Task HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
        {
            lock (_invokedHandlers)
            {
                _invokedHandlers.Add(_id);
            }
            return Task.CompletedTask;
        }
    }
}
