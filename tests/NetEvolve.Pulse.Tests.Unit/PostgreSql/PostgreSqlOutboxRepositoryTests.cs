namespace NetEvolve.Pulse.Tests.Unit.PostgreSql;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

public sealed class PostgreSqlOutboxRepositoryTests
{
    private const string ValidConnectionString = "Host=localhost;Database=Test;Username=postgres;Password=secret;";

    [Test]
    public async Task Constructor_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new PostgreSqlOutboxRepository(null!, Options.Create(new OutboxOptions()), TimeProvider.System))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new PostgreSqlOutboxRepository(string.Empty, Options.Create(new OutboxOptions()), TimeProvider.System)
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => new PostgreSqlOutboxRepository("   ", Options.Create(new OutboxOptions()), TimeProvider.System))
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new PostgreSqlOutboxRepository(ValidConnectionString, null!, TimeProvider.System))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithNullTimeProvider_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new PostgreSqlOutboxRepository(ValidConnectionString, Options.Create(new OutboxOptions()), null!)
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithValidArguments_CreatesInstance()
    {
        var repository = new PostgreSqlOutboxRepository(
            ValidConnectionString,
            Options.Create(new OutboxOptions()),
            TimeProvider.System
        );

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithTransactionScope_CreatesInstance()
    {
        var transactionScope = new PostgreSqlOutboxTransactionScope(null);

        var repository = new PostgreSqlOutboxRepository(
            ValidConnectionString,
            Options.Create(new OutboxOptions()),
            TimeProvider.System,
            transactionScope
        );

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithCustomSchema_CreatesInstance()
    {
        var options = new OutboxOptions { Schema = "custom" };

        var repository = new PostgreSqlOutboxRepository(
            ValidConnectionString,
            Options.Create(options),
            TimeProvider.System
        );

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithNullSchema_CreatesInstance()
    {
        var options = new OutboxOptions { Schema = null };

        var repository = new PostgreSqlOutboxRepository(
            ValidConnectionString,
            Options.Create(options),
            TimeProvider.System
        );

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithEmptySchema_CreatesInstance()
    {
        var options = new OutboxOptions { Schema = string.Empty };

        var repository = new PostgreSqlOutboxRepository(
            ValidConnectionString,
            Options.Create(options),
            TimeProvider.System
        );

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task AddAsync_WithNullMessage_ThrowsArgumentNullException()
    {
        var repository = new PostgreSqlOutboxRepository(
            ValidConnectionString,
            Options.Create(new OutboxOptions()),
            TimeProvider.System
        );

        _ = await Assert
            .That(async () => await repository.AddAsync(null!).ConfigureAwait(false))
            .Throws<ArgumentNullException>();
    }
}
