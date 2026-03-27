namespace NetEvolve.Pulse.SqlServer.Tests.Unit;

using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

public sealed class SqlServerEventOutboxTests
{
    [Test]
    public async Task Constructor_WithNullConnection_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new SqlServerEventOutbox(null!, Options.Create(new OutboxOptions()), TimeProvider.System))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        using var connection = new SqlConnection("Server=.;");

        _ = await Assert
            .That(() => new SqlServerEventOutbox(connection, null!, TimeProvider.System))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        using var connection = new SqlConnection("Server=.;");

        _ = await Assert
            .That(() => new SqlServerEventOutbox(connection, Options.Create(new OutboxOptions()), null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_WithValidArguments_CreatesInstance()
    {
        using var connection = new SqlConnection("Server=.;");

        var outbox = new SqlServerEventOutbox(connection, Options.Create(new OutboxOptions()), TimeProvider.System);

        _ = await Assert.That(outbox).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithTransaction_CreatesInstance()
    {
        using var connection = new SqlConnection("Server=.;");

        var outbox = new SqlServerEventOutbox(
            connection,
            Options.Create(new OutboxOptions()),
            TimeProvider.System,
            transaction: null
        );

        _ = await Assert.That(outbox).IsNotNull();
    }

    [Test]
    public async Task StoreAsync_WithNullMessage_ThrowsArgumentNullException()
    {
        using var connection = new SqlConnection("Server=.;");
        var outbox = new SqlServerEventOutbox(connection, Options.Create(new OutboxOptions()), TimeProvider.System);

        _ = await Assert
            .That(async () => await outbox.StoreAsync<TestEvent>(null!).ConfigureAwait(false))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task StoreAsync_WithLongCorrelationId_ThrowsInvalidOperationException()
    {
        using var connection = new SqlConnection("Server=.;");
        var outbox = new SqlServerEventOutbox(connection, Options.Create(new OutboxOptions()), TimeProvider.System);
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
