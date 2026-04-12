namespace NetEvolve.Pulse.Tests.Unit.PostgreSql;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("PostgreSql")]
public sealed class PostgreSqlOutboxManagementTests
{
    private const string ValidConnectionString = "Host=localhost;Database=Test;Username=postgres;Password=secret;";

    [Test]
    public async Task Constructor_WithNullConnectionString_ThrowsArgumentNullException(
        CancellationToken cancellationToken
    ) =>
        _ = await Assert
            .That(() => new PostgreSqlOutboxManagement(Options.Create(new OutboxOptions { ConnectionString = null })))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithEmptyConnectionString_ThrowsArgumentException(
        CancellationToken cancellationToken
    ) =>
        _ = await Assert
            .That(() =>
                new PostgreSqlOutboxManagement(Options.Create(new OutboxOptions { ConnectionString = string.Empty }))
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithWhitespaceConnectionString_ThrowsArgumentException(
        CancellationToken cancellationToken
    ) =>
        _ = await Assert
            .That(() => new PostgreSqlOutboxManagement(Options.Create(new OutboxOptions { ConnectionString = "   " })))
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException(CancellationToken cancellationToken) =>
        _ = await Assert.That(() => new PostgreSqlOutboxManagement(null!)).Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithValidArguments_CreatesInstance(CancellationToken cancellationToken)
    {
        var management = new PostgreSqlOutboxManagement(
            Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString })
        );

        _ = await Assert.That(management).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithNullSchema_UsesDefaultSchema(CancellationToken cancellationToken)
    {
        var options = Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString, Schema = null });

        var management = new PostgreSqlOutboxManagement(options);

        _ = await Assert.That(management).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithEmptySchema_UsesDefaultSchema(CancellationToken cancellationToken)
    {
        var options = Options.Create(
            new OutboxOptions { ConnectionString = ValidConnectionString, Schema = string.Empty }
        );

        var management = new PostgreSqlOutboxManagement(options);

        _ = await Assert.That(management).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithWhitespaceSchema_UsesDefaultSchema(CancellationToken cancellationToken)
    {
        var options = Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString, Schema = "   " });

        var management = new PostgreSqlOutboxManagement(options);

        _ = await Assert.That(management).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithCustomSchema_CreatesInstance(CancellationToken cancellationToken)
    {
        var options = Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString, Schema = "custom" });

        var management = new PostgreSqlOutboxManagement(options);

        _ = await Assert.That(management).IsNotNull();
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithNegativePageSize_ThrowsArgumentOutOfRangeException(
        CancellationToken cancellationToken
    )
    {
        var management = new PostgreSqlOutboxManagement(
            Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString })
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
        var management = new PostgreSqlOutboxManagement(
            Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString })
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
        var management = new PostgreSqlOutboxManagement(
            Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString })
        );

        _ = await Assert
            .That(async () => await management.GetDeadLetterMessagesAsync(page: -1).ConfigureAwait(false))
            .Throws<ArgumentOutOfRangeException>();
    }
}
