namespace NetEvolve.Pulse.Tests.Unit.SqlServer;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

public sealed class SqlServerOutboxRepositoryTests
{
    private const string ValidConnectionString = "Server=.;Database=Test;Integrated Security=true;";

    [Test]
    public async Task Constructor_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new SqlServerOutboxRepository(
                    Options.Create(new OutboxOptions { ConnectionString = null }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new SqlServerOutboxRepository(
                    Options.Create(new OutboxOptions { ConnectionString = string.Empty }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new SqlServerOutboxRepository(
                    Options.Create(new OutboxOptions { ConnectionString = "   " }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new SqlServerOutboxRepository(null!, TimeProvider.System))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithNullTimeProvider_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new SqlServerOutboxRepository(
                    Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString }),
                    null!
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithValidArguments_CreatesInstance()
    {
        var repository = new SqlServerOutboxRepository(
            Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString }),
            TimeProvider.System
        );

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithTransactionScope_CreatesInstance()
    {
        var transactionScope = new SqlServerOutboxTransactionScope(null);

        var repository = new SqlServerOutboxRepository(
            Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString }),
            TimeProvider.System,
            transactionScope
        );

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithCustomSchema_CreatesInstance()
    {
        var options = new OutboxOptions { ConnectionString = ValidConnectionString, Schema = "custom" };

        var repository = new SqlServerOutboxRepository(Options.Create(options), TimeProvider.System);

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithNullSchema_CreatesInstance()
    {
        var options = new OutboxOptions { ConnectionString = ValidConnectionString, Schema = null };

        var repository = new SqlServerOutboxRepository(Options.Create(options), TimeProvider.System);

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithEmptySchema_CreatesInstance()
    {
        var options = new OutboxOptions { ConnectionString = ValidConnectionString, Schema = string.Empty };

        var repository = new SqlServerOutboxRepository(Options.Create(options), TimeProvider.System);

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task AddAsync_WithNullMessage_ThrowsArgumentNullException()
    {
        var repository = new SqlServerOutboxRepository(
            Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString }),
            TimeProvider.System
        );

        _ = await Assert
            .That(async () => await repository.AddAsync(null!).ConfigureAwait(false))
            .Throws<ArgumentNullException>();
    }
}
