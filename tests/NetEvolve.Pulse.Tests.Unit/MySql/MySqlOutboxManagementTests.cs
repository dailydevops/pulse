namespace NetEvolve.Pulse.Tests.Unit.MySql;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("MySql")]
public sealed class MySqlOutboxManagementTests
{
    private const string ValidConnectionString = "Server=localhost;Database=Test;Uid=root;Pwd=secret;";

    [Test]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new MySqlOutboxManagement(null!, TimeProvider.System))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new MySqlOutboxManagement(
                    Options.Create(new OutboxOptions { ConnectionString = null }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new MySqlOutboxManagement(
                    Options.Create(new OutboxOptions { ConnectionString = string.Empty }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new MySqlOutboxManagement(
                    Options.Create(new OutboxOptions { ConnectionString = "   " }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithNullTimeProvider_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new MySqlOutboxManagement(
                    Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString }),
                    null!
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithValidArguments_CreatesInstance()
    {
        var management = new MySqlOutboxManagement(
            Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString }),
            TimeProvider.System
        );

        _ = await Assert.That(management).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithCustomTableName_CreatesInstance()
    {
        var options = new OutboxOptions { ConnectionString = ValidConnectionString, TableName = "CustomOutbox" };

        var management = new MySqlOutboxManagement(Options.Create(options), TimeProvider.System);

        _ = await Assert.That(management).IsNotNull();
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithNegativePageSize_ThrowsArgumentOutOfRangeException()
    {
        var management = new MySqlOutboxManagement(
            Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString }),
            TimeProvider.System
        );

        _ = await Assert
            .That(async () => await management.GetDeadLetterMessagesAsync(pageSize: -1).ConfigureAwait(false))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithZeroPageSize_ThrowsArgumentOutOfRangeException()
    {
        var management = new MySqlOutboxManagement(
            Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString }),
            TimeProvider.System
        );

        _ = await Assert
            .That(async () => await management.GetDeadLetterMessagesAsync(pageSize: 0).ConfigureAwait(false))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithNegativePage_ThrowsArgumentOutOfRangeException()
    {
        var management = new MySqlOutboxManagement(
            Options.Create(new OutboxOptions { ConnectionString = ValidConnectionString }),
            TimeProvider.System
        );

        _ = await Assert
            .That(async () => await management.GetDeadLetterMessagesAsync(page: -1).ConfigureAwait(false))
            .Throws<ArgumentOutOfRangeException>();
    }
}
