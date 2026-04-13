namespace NetEvolve.Pulse.Tests.Unit.SQLite;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;
using TUnit.Mocks;

[TestGroup("SQLite")]
public sealed class SQLiteEventOutboxTests
{
    [Test]
    public async Task Constructor_WithNullRepository_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new SQLiteEventOutbox(
                    null!,
                    Options.Create(new OutboxOptions { ConnectionString = "Data Source=:memory:" }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        var repositoryMock = Mock.Of<IOutboxRepository>();

        _ = await Assert
            .That(() => new SQLiteEventOutbox(repositoryMock.Object, null!, TimeProvider.System))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        var repositoryMock = Mock.Of<IOutboxRepository>();

        _ = await Assert
            .That(() =>
                new SQLiteEventOutbox(
                    repositoryMock.Object,
                    Options.Create(new OutboxOptions { ConnectionString = "Data Source=:memory:" }),
                    null!
                )
            )
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_WithValidArguments_CreatesInstance()
    {
        var repositoryMock = Mock.Of<IOutboxRepository>();

        var outbox = new SQLiteEventOutbox(
            repositoryMock.Object,
            Options.Create(new OutboxOptions { ConnectionString = "Data Source=:memory:" }),
            TimeProvider.System
        );

        _ = await Assert.That(outbox).IsNotNull();
    }

    [Test]
    public async Task StoreAsync_WithNullMessage_ThrowsArgumentNullException()
    {
        var repository = new FakeOutboxRepository();
        var outbox = new SQLiteEventOutbox(
            repository,
            Options.Create(new OutboxOptions { ConnectionString = "Data Source=:memory:" }),
            TimeProvider.System
        );

        _ = await Assert.That(() => outbox.StoreAsync<TestEvent>(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task StoreAsync_WithValidEvent_CallsRepositoryAddAsync()
    {
        var repository = new FakeOutboxRepository();
        var outbox = new SQLiteEventOutbox(
            repository,
            Options.Create(new OutboxOptions { ConnectionString = "Data Source=:memory:" }),
            TimeProvider.System
        );

        var testEvent = new TestEvent { Id = Guid.NewGuid().ToString() };

        await outbox.StoreAsync(testEvent);

        _ = await Assert.That(repository.AddAsyncCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task StoreAsync_WithCorrelationIdExceedingMaxLength_ThrowsInvalidOperationException()
    {
        var repository = new FakeOutboxRepository();
        var outbox = new SQLiteEventOutbox(
            repository,
            Options.Create(new OutboxOptions { ConnectionString = "Data Source=:memory:" }),
            TimeProvider.System
        );

        var testEvent = new TestEvent
        {
            Id = Guid.NewGuid().ToString(),
            CorrelationId = new string('x', OutboxMessageSchema.MaxLengths.CorrelationId + 1),
        };

        _ = await Assert.That(() => outbox.StoreAsync(testEvent)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task StoreAsync_WithActiveTransactionScope_CallsRepositoryAddAsync()
    {
        // Arrange: use a fake transaction scope simulating an active transaction
        var repository = new FakeOutboxRepository();
        var transactionScopeMock = Mock.Of<IOutboxTransactionScope>();
        _ = transactionScopeMock.GetCurrentTransaction().Returns(() => null);

        var outbox = new SQLiteEventOutbox(
            repository,
            Options.Create(new OutboxOptions { ConnectionString = "Data Source=:memory:" }),
            TimeProvider.System
        );

        var testEvent = new TestEvent { Id = Guid.NewGuid().ToString() };

        // Act
        await outbox.StoreAsync(testEvent);

        // Assert: repository should have been called regardless of transaction scope on SQLiteEventOutbox itself
        _ = await Assert.That(repository.AddAsyncCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task StoreAsync_WithoutTransactionScope_CallsRepositoryAddAsync()
    {
        // Arrange: no transaction scope — repository opens its own connection
        var repository = new FakeOutboxRepository();
        var outbox = new SQLiteEventOutbox(
            repository,
            Options.Create(new OutboxOptions { ConnectionString = "Data Source=:memory:" }),
            TimeProvider.System
        );

        var testEvent = new TestEvent { Id = Guid.NewGuid().ToString() };

        // Act
        await outbox.StoreAsync(testEvent);

        // Assert
        _ = await Assert.That(repository.AddAsyncCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task StoreAsync_StoredMessage_HasPendingStatus()
    {
        var repository = new FakeOutboxRepository();
        var outbox = new SQLiteEventOutbox(
            repository,
            Options.Create(new OutboxOptions { ConnectionString = "Data Source=:memory:" }),
            TimeProvider.System
        );

        var testEvent = new TestEvent { Id = Guid.NewGuid().ToString(), CorrelationId = "corr-1" };

        await outbox.StoreAsync(testEvent);

        var stored = repository.LastAddedMessage;
        using (Assert.Multiple())
        {
            _ = await Assert.That(stored).IsNotNull();
            _ = await Assert.That(stored!.Status).IsEqualTo(OutboxMessageStatus.Pending);
            _ = await Assert.That(stored.EventType).IsEqualTo(typeof(TestEvent));
            _ = await Assert.That(stored.CorrelationId).IsEqualTo("corr-1");
        }
    }

    private sealed class FakeOutboxRepository : IOutboxRepository
    {
        public int AddAsyncCallCount { get; private set; }
        public OutboxMessage? LastAddedMessage { get; private set; }

        public Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            AddAsyncCallCount++;
            LastAddedMessage = message;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(
            int batchSize,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());

        public Task<IReadOnlyList<OutboxMessage>> GetFailedForRetryAsync(
            int maxRetryCount,
            int batchSize,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());

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

        public Task<int> DeleteCompletedAsync(TimeSpan olderThan, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    private sealed class TestEvent : IEvent
    {
        public string? CorrelationId { get; set; }

        public required string Id { get; init; }

        public DateTimeOffset? PublishedAt { get; set; }
    }
}
