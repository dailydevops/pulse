namespace NetEvolve.Pulse.PostgreSql.Tests.Unit;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

public sealed class PostgreSqlOutboxManagementTests
{
    private const string ValidConnectionString = "Host=localhost;Database=Test;Username=postgres;Password=secret;";

    [Test]
    public async Task Constructor_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new PostgreSqlOutboxManagement(null!, Options.Create(new OutboxOptions())))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => new PostgreSqlOutboxManagement(string.Empty, Options.Create(new OutboxOptions())))
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => new PostgreSqlOutboxManagement("   ", Options.Create(new OutboxOptions())))
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new PostgreSqlOutboxManagement(ValidConnectionString, null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithValidArguments_CreatesInstance()
    {
        var management = new PostgreSqlOutboxManagement(ValidConnectionString, Options.Create(new OutboxOptions()));

        _ = await Assert.That(management).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithNullSchema_UsesDefaultSchema()
    {
        var options = Options.Create(new OutboxOptions { Schema = null });

        var management = new PostgreSqlOutboxManagement(ValidConnectionString, options);

        _ = await Assert.That(management).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithEmptySchema_UsesDefaultSchema()
    {
        var options = Options.Create(new OutboxOptions { Schema = string.Empty });

        var management = new PostgreSqlOutboxManagement(ValidConnectionString, options);

        _ = await Assert.That(management).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithWhitespaceSchema_UsesDefaultSchema()
    {
        var options = Options.Create(new OutboxOptions { Schema = "   " });

        var management = new PostgreSqlOutboxManagement(ValidConnectionString, options);

        _ = await Assert.That(management).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithCustomSchema_CreatesInstance()
    {
        var options = Options.Create(new OutboxOptions { Schema = "custom" });

        var management = new PostgreSqlOutboxManagement(ValidConnectionString, options);

        _ = await Assert.That(management).IsNotNull();
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithNegativePageSize_ThrowsArgumentOutOfRangeException()
    {
        var management = new PostgreSqlOutboxManagement(ValidConnectionString, Options.Create(new OutboxOptions()));

        _ = await Assert
            .That(async () => await management.GetDeadLetterMessagesAsync(pageSize: -1).ConfigureAwait(false))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithZeroPageSize_ThrowsArgumentOutOfRangeException()
    {
        var management = new PostgreSqlOutboxManagement(ValidConnectionString, Options.Create(new OutboxOptions()));

        _ = await Assert
            .That(async () => await management.GetDeadLetterMessagesAsync(pageSize: 0).ConfigureAwait(false))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_WithNegativePage_ThrowsArgumentOutOfRangeException()
    {
        var management = new PostgreSqlOutboxManagement(ValidConnectionString, Options.Create(new OutboxOptions()));

        _ = await Assert
            .That(async () => await management.GetDeadLetterMessagesAsync(page: -1).ConfigureAwait(false))
            .Throws<ArgumentOutOfRangeException>();
    }
}
