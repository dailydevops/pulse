namespace NetEvolve.Pulse.Tests.Unit.Outbox;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// Unit tests for <see cref="OutboxEventStore"/>.
/// Pins down constructor null-arg invariants, CorrelationId/CausationId length validation,
/// timestamp population and repository delegation behavior.
/// </summary>
[TestGroup("Outbox")]
public sealed class OutboxEventStoreTests
{
    [Test]
    public async Task Constructor_WithNullRepository_ThrowsArgumentNullException()
    {
        IOutboxRepository? repository = null;

        _ = Assert.Throws<ArgumentNullException>(
            "repository",
            () => _ = new OutboxEventStore(repository!, TimeProvider.System, new StubPayloadSerializer())
        );

        await Task.CompletedTask.ConfigureAwait(false);
    }

    [Test]
    public async Task Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        var repository = new TrackingOutboxRepository();

        _ = Assert.Throws<ArgumentNullException>(
            "timeProvider",
            () => _ = new OutboxEventStore(repository, null!, new StubPayloadSerializer())
        );

        await Task.CompletedTask.ConfigureAwait(false);
    }

    [Test]
    public async Task Constructor_WithNullPayloadSerializer_ThrowsArgumentNullException()
    {
        var repository = new TrackingOutboxRepository();

        _ = Assert.Throws<ArgumentNullException>(
            "payloadSerializer",
            () => _ = new OutboxEventStore(repository, TimeProvider.System, null!)
        );

        await Task.CompletedTask.ConfigureAwait(false);
    }

    [Test]
    public async Task StoreAsync_WithNullMessage_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        var store = CreateStore(out _);

        _ = await Assert
            .That(async () => await store.StoreAsync<TestEvent>(null!, cancellationToken).ConfigureAwait(false))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task StoreAsync_WithValidEvent_AddsMessageToRepository(CancellationToken cancellationToken)
    {
        var store = CreateStore(out var repository);
        var evt = new TestEvent();

        await store.StoreAsync(evt, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(repository.Added).HasSingleItem();
            _ = await Assert.That(repository.Added[0].EventType).IsEqualTo(typeof(TestEvent));
            _ = await Assert.That(repository.Added[0].Status).IsEqualTo(OutboxMessageStatus.Pending);
            _ = await Assert.That(repository.Added[0].CorrelationId).IsEqualTo(evt.CorrelationId);
            _ = await Assert.That(repository.Added[0].CausationId).IsEqualTo(evt.CausationId);
        }
    }

    [Test]
    public async Task StoreAsync_SetsCreatedAtAndUpdatedAtFromTimeProvider(CancellationToken cancellationToken)
    {
        var store = CreateStore(out var repository);
        var evt = new TestEvent();

        await store.StoreAsync(evt, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(repository.Added).HasSingleItem();
        var message = repository.Added[0];
        // CreatedAt and UpdatedAt should be the same single snapshot from TimeProvider.GetUtcNow()
        _ = await Assert.That(message.CreatedAt).IsEqualTo(message.UpdatedAt);
    }

    [Test]
    public async Task StoreAsync_WithCorrelationIdAtMaxLength_Succeeds(CancellationToken cancellationToken)
    {
        var store = CreateStore(out var repository);
        var evt = new TestEvent { CorrelationId = new string('c', OutboxMessageSchema.MaxLengths.CorrelationId) };

        await store.StoreAsync(evt, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(repository.Added).HasSingleItem();
        _ = await Assert.That(repository.Added[0].CorrelationId).IsEqualTo(evt.CorrelationId);
    }

    [Test]
    public async Task StoreAsync_WithCorrelationIdTooLong_ThrowsInvalidOperationException(
        CancellationToken cancellationToken
    )
    {
        var store = CreateStore(out var repository);
        var evt = new TestEvent { CorrelationId = new string('c', OutboxMessageSchema.MaxLengths.CorrelationId + 1) };

        _ = await Assert
            .That(async () => await store.StoreAsync(evt, cancellationToken).ConfigureAwait(false))
            .Throws<InvalidOperationException>();

        _ = await Assert.That(repository.Added).IsEmpty();
    }

    [Test]
    public async Task StoreAsync_WithCausationIdAtMaxLength_Succeeds(CancellationToken cancellationToken)
    {
        var store = CreateStore(out var repository);
        var evt = new TestEvent { CausationId = new string('c', OutboxMessageSchema.MaxLengths.CausationId) };

        await store.StoreAsync(evt, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(repository.Added).HasSingleItem();
        _ = await Assert.That(repository.Added[0].CausationId).IsEqualTo(evt.CausationId);
    }

    [Test]
    public async Task StoreAsync_WithCausationIdTooLong_ThrowsInvalidOperationException(
        CancellationToken cancellationToken
    )
    {
        var store = CreateStore(out var repository);
        var evt = new TestEvent { CausationId = new string('c', OutboxMessageSchema.MaxLengths.CausationId + 1) };

        _ = await Assert
            .That(async () => await store.StoreAsync(evt, cancellationToken).ConfigureAwait(false))
            .Throws<InvalidOperationException>();

        _ = await Assert.That(repository.Added).IsEmpty();
    }

    [Test]
    public async Task StoreAsync_StoresPayloadFromSerializer(CancellationToken cancellationToken)
    {
        var store = CreateStore(out var repository);
        var evt = new TestEvent();

        await store.StoreAsync(evt, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(repository.Added).HasSingleItem();
        _ = await Assert.That(repository.Added[0].Payload).IsNotNull();
        _ = await Assert.That(repository.Added[0].Payload.Length).IsGreaterThan(0);
    }

    private static OutboxEventStore CreateStore(out TrackingOutboxRepository repository)
    {
        repository = new TrackingOutboxRepository();
        return new OutboxEventStore(repository, TimeProvider.System, new StubPayloadSerializer());
    }

    private sealed class TestEvent : IEvent
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }

    /// <summary>
    /// Minimal stub serializer that returns a deterministic non-empty payload for any input.
    /// Used to keep the OutboxEventStore tests free of System.Text.Json options-pattern wiring.
    /// </summary>
    private sealed class StubPayloadSerializer : IPayloadSerializer
    {
        public string Serialize<T>(T value) => "stub";

        public string Serialize(object value, Type type) => "stub-typed";

        public byte[] SerializeToBytes<T>(T value) => [1, 2, 3];

        public T? Deserialize<T>(string payload) => default;

        public T? Deserialize<T>(byte[] payload) => default;
    }

    private sealed class TrackingOutboxRepository : IOutboxRepository
    {
        public List<OutboxMessage> Added { get; } = [];

        public Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            Added.Add(message);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(
            int batchSize,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<OutboxMessage>>([]);

        public Task MarkAsCompletedAsync(Guid messageId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task MarkAsFailedAsync(
            Guid messageId,
            string errorMessage,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public Task MarkAsDeadLetterAsync(
            Guid messageId,
            string errorMessage,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public Task<IReadOnlyList<OutboxMessage>> GetFailedForRetryAsync(
            int maxRetryCount,
            int batchSize,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<OutboxMessage>>([]);

        public Task<long> GetPendingCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0L);

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<int> DeleteCompletedAsync(TimeSpan olderThan, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }
}
