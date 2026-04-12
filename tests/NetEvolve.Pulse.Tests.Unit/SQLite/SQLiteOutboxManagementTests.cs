namespace NetEvolve.Pulse.Tests.Unit.SQLite;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("SQLite")]
public sealed class SQLiteOutboxManagementTests
{
    [Test]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException(CancellationToken cancellationToken) =>
        _ = await Assert
            .That(() => new SQLiteOutboxManagement(null!, TimeProvider.System))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithNullTimeProvider_ThrowsArgumentNullException(
        CancellationToken cancellationToken
    ) =>
        _ = await Assert
            .That(() => new SQLiteOutboxManagement(Options.Create(new OutboxOptions()), null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithEmptyConnectionString_ThrowsArgumentException(
        CancellationToken cancellationToken
    ) =>
        _ = await Assert
            .That(() =>
                new SQLiteOutboxManagement(
                    Options.Create(new OutboxOptions { ConnectionString = string.Empty }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithWhitespaceConnectionString_ThrowsArgumentException(
        CancellationToken cancellationToken
    ) =>
        _ = await Assert
            .That(() =>
                new SQLiteOutboxManagement(
                    Options.Create(new OutboxOptions { ConnectionString = "   " }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithValidOptions_CreatesInstance(CancellationToken cancellationToken)
    {
        var management = new SQLiteOutboxManagement(
            Options.Create(new OutboxOptions { ConnectionString = "Data Source=:memory:", EnableWalMode = false }),
            TimeProvider.System
        );

        _ = await Assert.That(management).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithCustomTableName_CreatesInstance(CancellationToken cancellationToken)
    {
        var options = new OutboxOptions
        {
            ConnectionString = "Data Source=:memory:",
            EnableWalMode = false,
            TableName = "CustomTable",
        };

        var management = new SQLiteOutboxManagement(Options.Create(options), TimeProvider.System);

        _ = await Assert.That(management).IsNotNull();
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithNegativePageSize_ThrowsArgumentOutOfRangeException(
        CancellationToken cancellationToken
    )
    {
        var management = new SQLiteOutboxManagement(
            Options.Create(new OutboxOptions { ConnectionString = "Data Source=:memory:", EnableWalMode = false }),
            TimeProvider.System
        );

        _ = await Assert
            .That(async () => await management.GetDeadLetterMessagesAsync(pageSize: -1).ConfigureAwait(false))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithZeroPageSize_ThrowsArgumentOutOfRangeException(
        CancellationToken cancellationToken
    )
    {
        var management = new SQLiteOutboxManagement(
            Options.Create(new OutboxOptions { ConnectionString = "Data Source=:memory:", EnableWalMode = false }),
            TimeProvider.System
        );

        _ = await Assert
            .That(async () => await management.GetDeadLetterMessagesAsync(pageSize: 0).ConfigureAwait(false))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithNegativePage_ThrowsArgumentOutOfRangeException(
        CancellationToken cancellationToken
    )
    {
        var management = new SQLiteOutboxManagement(
            Options.Create(new OutboxOptions { ConnectionString = "Data Source=:memory:", EnableWalMode = false }),
            TimeProvider.System
        );

        _ = await Assert
            .That(async () => await management.GetDeadLetterMessagesAsync(page: -1).ConfigureAwait(false))
            .Throws<ArgumentOutOfRangeException>();
    }
}
