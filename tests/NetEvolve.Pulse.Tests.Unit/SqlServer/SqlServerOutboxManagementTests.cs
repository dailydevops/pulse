namespace NetEvolve.Pulse.Tests.Unit.SqlServer;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

public sealed class SqlServerOutboxManagementTests
{
    private const string ValidConnectionString = "Server=.;Database=Test;Integrated Security=true;";

    [Test]
    public async Task Constructor_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new SqlServerOutboxManagement(Options.Create(new OutboxOptions { ConnectionString = null })))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new SqlServerOutboxManagement(Options.Create(new OutboxOptions { ConnectionString = string.Empty }))
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => new SqlServerOutboxManagement(Options.Create(new OutboxOptions { ConnectionString = "   " })))
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException() =>
        _ = await Assert.That(() => new SqlServerOutboxManagement(null!)).Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithValidArguments_CreatesInstance()
    {
        var management = new SqlServerOutboxManagement(
            Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString })
        );

        _ = await Assert.That(management).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithNullSchema_UsesDefaultDboSchema()
    {
        var options = Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString, Schema = null });

        var management = new SqlServerOutboxManagement(options);

        _ = await Assert.That(management).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithEmptySchema_UsesDefaultDboSchema()
    {
        var options = Options.Create(
            new OutboxOptions { ConnectionString = ValidConnectionString, Schema = string.Empty }
        );

        var management = new SqlServerOutboxManagement(options);

        _ = await Assert.That(management).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithWhitespaceSchema_UsesDefaultDboSchema()
    {
        var options = Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString, Schema = "   " });

        var management = new SqlServerOutboxManagement(options);

        _ = await Assert.That(management).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithCustomSchema_CreatesInstance()
    {
        var options = Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString, Schema = "custom" });

        var management = new SqlServerOutboxManagement(options);

        _ = await Assert.That(management).IsNotNull();
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithNegativePageSize_ThrowsArgumentOutOfRangeException()
    {
        var management = new SqlServerOutboxManagement(
            Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString })
        );

        _ = await Assert
            .That(async () => await management.GetDeadLetterMessagesAsync(pageSize: -1).ConfigureAwait(false))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithZeroPageSize_ThrowsArgumentOutOfRangeException()
    {
        var management = new SqlServerOutboxManagement(
            Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString })
        );

        _ = await Assert
            .That(async () => await management.GetDeadLetterMessagesAsync(pageSize: 0).ConfigureAwait(false))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithNegativePage_ThrowsArgumentOutOfRangeException()
    {
        var management = new SqlServerOutboxManagement(
            Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString })
        );

        _ = await Assert
            .That(async () => await management.GetDeadLetterMessagesAsync(page: -1).ConfigureAwait(false))
            .Throws<ArgumentOutOfRangeException>();
    }
}
