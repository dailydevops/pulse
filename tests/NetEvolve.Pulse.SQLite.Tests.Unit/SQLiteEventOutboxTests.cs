namespace NetEvolve.Pulse.SQLite.Tests.Unit;

using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

public sealed class SQLiteEventOutboxTests
{
    [Test]
    public async Task Constructor_WithNullConnection_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new SQLiteEventOutbox(null!, Options.Create(new SQLiteOutboxOptions()), TimeProvider.System))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");

        _ = await Assert
            .That(() => new SQLiteEventOutbox(connection, null!, TimeProvider.System))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");

        _ = await Assert
            .That(() => new SQLiteEventOutbox(connection, Options.Create(new SQLiteOutboxOptions()), null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_WithValidArguments_CreatesInstance()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");

        var outbox = new SQLiteEventOutbox(connection, Options.Create(new SQLiteOutboxOptions()), TimeProvider.System);

        _ = await Assert.That(outbox).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithTransaction_CreatesInstance()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");

        var outbox = new SQLiteEventOutbox(
            connection,
            Options.Create(new SQLiteOutboxOptions()),
            TimeProvider.System,
            transaction: null
        );

        _ = await Assert.That(outbox).IsNotNull();
    }

    [Test]
    public async Task StoreAsync_WithNullMessage_ThrowsArgumentNullException()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        var outbox = new SQLiteEventOutbox(connection, Options.Create(new SQLiteOutboxOptions()), TimeProvider.System);

        _ = await Assert
            .That(async () => await outbox.StoreAsync<TestEvent>(null!).ConfigureAwait(false))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task StoreAsync_WithLongCorrelationId_ThrowsInvalidOperationException()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        var outbox = new SQLiteEventOutbox(connection, Options.Create(new SQLiteOutboxOptions()), TimeProvider.System);
        var message = new TestEvent
        {
            CorrelationId = new string('x', OutboxMessageSchema.MaxLengths.CorrelationId + 1),
        };

        _ = await Assert
            .That(async () => await outbox.StoreAsync(message).ConfigureAwait(false))
            .Throws<InvalidOperationException>();
    }

    private sealed record TestEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
