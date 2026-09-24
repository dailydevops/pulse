namespace NetEvolve.Pulse.Tests.Unit.PostgreSql;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("PostgreSql")]
public sealed class PostgreSqlOutboxRepositoryTests
{
    private const string ValidConnectionString = "Host=localhost;Database=Test;Username=postgres;Password=secret;";

    [Test]
    public async Task Constructor_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new PostgreSqlOutboxRepository(
                    Options.Create(new OutboxOptions { ConnectionString = null }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new PostgreSqlOutboxRepository(
                    Options.Create(new OutboxOptions { ConnectionString = string.Empty }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new PostgreSqlOutboxRepository(
                    Options.Create(new OutboxOptions { ConnectionString = "   " }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new PostgreSqlOutboxRepository(null!, TimeProvider.System))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithNullTimeProvider_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new PostgreSqlOutboxRepository(
                    Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString }),
                    null!
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithValidArguments_CreatesInstance()
    {
        var repository = new PostgreSqlOutboxRepository(
            Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString }),
            TimeProvider.System
        );

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithTransactionScope_CreatesInstance()
    {
        var transactionScope = new PostgreSqlOutboxTransactionScope(null);

        var repository = new PostgreSqlOutboxRepository(
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

        var repository = new PostgreSqlOutboxRepository(Options.Create(options), TimeProvider.System);

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithZeroProcessingLeaseTimeout_ThrowsArgumentOutOfRangeException() =>
        _ = await Assert
            .That(() =>
                new PostgreSqlOutboxRepository(
                    Options.Create(
                        new OutboxOptions
                        {
                            ConnectionString = ValidConnectionString,
                            ProcessingLeaseTimeout = TimeSpan.Zero,
                        }
                    ),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentOutOfRangeException>();

    [Test]
    public async Task Constructor_WithNegativeProcessingLeaseTimeout_ThrowsArgumentOutOfRangeException() =>
        _ = await Assert
            .That(() =>
                new PostgreSqlOutboxRepository(
                    Options.Create(
                        new OutboxOptions
                        {
                            ConnectionString = ValidConnectionString,
                            ProcessingLeaseTimeout = TimeSpan.FromSeconds(-1),
                        }
                    ),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentOutOfRangeException>();

    [Test]
    public async Task Constructor_WithNullSchema_CreatesInstance()
    {
        var options = new OutboxOptions { ConnectionString = ValidConnectionString, Schema = null };

        var repository = new PostgreSqlOutboxRepository(Options.Create(options), TimeProvider.System);

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithEmptySchema_CreatesInstance()
    {
        var options = new OutboxOptions { ConnectionString = ValidConnectionString, Schema = string.Empty };

        var repository = new PostgreSqlOutboxRepository(Options.Create(options), TimeProvider.System);

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task AddAsync_WithNullMessage_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var repository = new PostgreSqlOutboxRepository(
            Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString }),
            TimeProvider.System
        );

        _ = await Assert
            .That(async () => await repository.AddAsync(null!, cancellationToken).ConfigureAwait(false))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task MarkAsCompletedAsync_Batch_WithNullMessageIds_ThrowsArgumentNullException(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var repository = new PostgreSqlOutboxRepository(
            Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString }),
            TimeProvider.System
        );

        _ = await Assert
            .That(async () => await repository.MarkAsCompletedAsync(null!, cancellationToken).ConfigureAwait(false))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task MarkAsCompletedAsync_Batch_WithEmptyMessageIds_CompletesWithoutConnecting(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var repository = new PostgreSqlOutboxRepository(
            Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString }),
            TimeProvider.System
        );

        await repository.MarkAsCompletedAsync(Array.Empty<Guid>(), cancellationToken).ConfigureAwait(false);
    }

    [Test]
    public async Task MarkAsFailedAsync_Batch_WithNullMessageIds_ThrowsArgumentNullException(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var repository = new PostgreSqlOutboxRepository(
            Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString }),
            TimeProvider.System
        );

        _ = await Assert
            .That(async () =>
                await repository.MarkAsFailedAsync(null!, "error", cancellationToken).ConfigureAwait(false)
            )
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task MarkAsFailedAsync_Batch_WithEmptyMessageIds_CompletesWithoutConnecting(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var repository = new PostgreSqlOutboxRepository(
            Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString }),
            TimeProvider.System
        );

        await repository.MarkAsFailedAsync(Array.Empty<Guid>(), "error", cancellationToken).ConfigureAwait(false);
    }
}
