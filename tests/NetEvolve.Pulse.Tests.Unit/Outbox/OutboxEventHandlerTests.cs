namespace NetEvolve.Pulse.Tests.Unit.Outbox;

using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

/// <summary>
/// Unit tests for <see cref="OutboxEventHandler{TEvent}"/>.
/// Tests outbox delegation and in-process event exclusion semantics.
/// </summary>
[TestGroup("Outbox")]
public sealed class OutboxEventHandlerTests
{
    [Test]
    public async Task HandleAsync_WithRegularEvent_StoresEventInOutbox(CancellationToken cancellationToken)
    {
        var outbox = new TrackingEventOutbox();
        var handler = new OutboxEventHandler<TestRegularEvent>(outbox);
        var @event = new TestRegularEvent();

        await handler.HandleAsync(@event, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(outbox.StoredEvents).HasSingleItem();
            _ = await Assert.That(outbox.StoredEvents[0]).IsSameReferenceAs(@event);
        }
    }

    [Test]
    public async Task HandleAsync_WithInProcessEvent_SkipsOutbox(CancellationToken cancellationToken)
    {
        var outbox = new TrackingEventOutbox();
        var handler = new OutboxEventHandler<TestInProcessEvent>(outbox);
        var @event = new TestInProcessEvent();

        await handler.HandleAsync(@event, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(outbox.StoredEvents).IsEmpty();
    }

    [Test]
    public async Task HandleAsync_WithInProcessEventAndHandleInProcessFalse_StoresEventInOutbox(
        CancellationToken cancellationToken
    )
    {
        var outbox = new TrackingEventOutbox();
        var handler = new OutboxEventHandler<TestOptOutInProcessEvent>(outbox);
        var @event = new TestOptOutInProcessEvent();

        await handler.HandleAsync(@event, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(outbox.StoredEvents).HasSingleItem();
            _ = await Assert.That(outbox.StoredEvents[0]).IsSameReferenceAs(@event);
        }
    }

    [Test]
    public async Task HandleAsync_WithCancellationToken_PassesTokenToOutbox(CancellationToken cancellationToken)
    {
        var outbox = new TrackingEventOutbox();
        var handler = new OutboxEventHandler<TestRegularEvent>(outbox);
        var @event = new TestRegularEvent();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        await handler.HandleAsync(@event, cts.Token).ConfigureAwait(false);

        _ = await Assert.That(outbox.LastCancellationToken).IsEqualTo(cts.Token);
    }

    [Test]
    public async Task HandleAsync_WithCancelledToken_PropagatesCancellation(CancellationToken cancellationToken)
    {
        var outbox = new TrackingEventOutbox();
        var handler = new OutboxEventHandler<TestRegularEvent>(outbox);
        var @event = new TestRegularEvent();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await cts.CancelAsync().ConfigureAwait(false);

        _ = await Assert.That(() => handler.HandleAsync(@event, cts.Token)).Throws<OperationCanceledException>();
    }

    private sealed class TrackingEventOutbox : IEventOutbox
    {
        public List<IEvent> StoredEvents { get; } = [];
        public CancellationToken LastCancellationToken { get; private set; }

        public Task StoreAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default)
            where TEvent : IEvent
        {
            cancellationToken.ThrowIfCancellationRequested();
            StoredEvents.Add(message);
            LastCancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class TestRegularEvent : IEvent
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class TestInProcessEvent : IEventInProcess
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class TestOptOutInProcessEvent : IEventInProcess
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
        public bool HandleInProcess => false;
    }
}
