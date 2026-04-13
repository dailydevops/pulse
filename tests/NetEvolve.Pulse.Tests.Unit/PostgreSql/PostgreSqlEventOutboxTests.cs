namespace NetEvolve.Pulse.Tests.Unit.PostgreSql;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("PostgreSql")]
public sealed class PostgreSqlEventOutboxTests
{
    private const string ValidConnectionString = "Host=localhost;Database=Test;Username=postgres;Password=secret;";

    [Test]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new PostgreSqlEventOutbox(null!, TimeProvider.System))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new PostgreSqlEventOutbox(
                    Options.Create(new OutboxOptions { ConnectionString = null }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new PostgreSqlEventOutbox(
                    Options.Create(new OutboxOptions { ConnectionString = string.Empty }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new PostgreSqlEventOutbox(
                    Options.Create(new OutboxOptions { ConnectionString = "   " }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithNullTimeProvider_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new PostgreSqlEventOutbox(
                    Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString }),
                    null!
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithValidArguments_CreatesInstance()
    {
        var outbox = new PostgreSqlEventOutbox(
            Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString }),
            TimeProvider.System
        );

        _ = await Assert.That(outbox).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithTransactionScope_CreatesInstance()
    {
        var transactionScope = new PostgreSqlOutboxTransactionScope(null);

        var outbox = new PostgreSqlEventOutbox(
            Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString }),
            TimeProvider.System,
            transactionScope
        );

        _ = await Assert.That(outbox).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithCustomSchema_CreatesInstance()
    {
        var options = new OutboxOptions { ConnectionString = ValidConnectionString, Schema = "custom" };

        var outbox = new PostgreSqlEventOutbox(Options.Create(options), TimeProvider.System);

        _ = await Assert.That(outbox).IsNotNull();
    }

    [Test]
    public async Task StoreAsync_WithNullMessage_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        var outbox = new PostgreSqlEventOutbox(
            Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString }),
            TimeProvider.System
        );

        _ = await Assert
            .That(async () => await outbox.StoreAsync<TestEvent>(null!, cancellationToken).ConfigureAwait(false))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task StoreAsync_WithCorrelationIdTooLong_ThrowsInvalidOperationException(
        CancellationToken cancellationToken
    )
    {
        var outbox = new PostgreSqlEventOutbox(
            Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString }),
            TimeProvider.System
        );

        var message = new TestEvent
        {
            CorrelationId = new string('x', OutboxMessageSchema.MaxLengths.CorrelationId + 1),
        };

        _ = await Assert
            .That(async () => await outbox.StoreAsync(message, cancellationToken).ConfigureAwait(false))
            .Throws<InvalidOperationException>();
    }

    private sealed class TestEvent : IEvent
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string? CorrelationId { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
