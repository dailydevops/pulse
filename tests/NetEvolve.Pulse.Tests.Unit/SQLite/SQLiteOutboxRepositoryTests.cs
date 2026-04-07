namespace NetEvolve.Pulse.Tests.Unit.SQLite;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("SQLite")]
public sealed class SQLiteOutboxRepositoryTests
{
    [Test]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new SQLiteOutboxRepository(null!, TimeProvider.System))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithNullTimeProvider_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new SQLiteOutboxRepository(Options.Create(new OutboxOptions()), null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new SQLiteOutboxRepository(
                    Options.Create(new OutboxOptions { ConnectionString = string.Empty }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new SQLiteOutboxRepository(
                    Options.Create(new OutboxOptions { ConnectionString = "   " }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithValidOptions_CreatesInstance()
    {
        var repository = new SQLiteOutboxRepository(
            Options.Create(new OutboxOptions { ConnectionString = "Data Source=:memory:", EnableWalMode = false }),
            TimeProvider.System
        );

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithTransactionScope_CreatesInstance()
    {
        var transactionScope = new SQLiteOutboxTransactionScope(null);

        var repository = new SQLiteOutboxRepository(
            Options.Create(new OutboxOptions { ConnectionString = "Data Source=:memory:", EnableWalMode = false }),
            TimeProvider.System,
            transactionScope
        );

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithCustomTableName_CreatesInstance()
    {
        var options = new OutboxOptions
        {
            ConnectionString = "Data Source=:memory:",
            EnableWalMode = false,
            TableName = "CustomTable",
        };

        var repository = new SQLiteOutboxRepository(Options.Create(options), TimeProvider.System);

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task AddAsync_WithNullMessage_ThrowsArgumentNullException()
    {
        var repository = new SQLiteOutboxRepository(
            Options.Create(new OutboxOptions { ConnectionString = "Data Source=:memory:", EnableWalMode = false }),
            TimeProvider.System
        );

        _ = await Assert
            .That(async () => await repository.AddAsync(null!).ConfigureAwait(false))
            .Throws<ArgumentNullException>();
    }
}
