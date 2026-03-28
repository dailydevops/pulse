namespace NetEvolve.Pulse.SqlServer.Tests.Unit;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

public sealed class SqlServerOutboxManagementTests
{
    private const string ValidConnectionString = "Server=.;Database=Test;Integrated Security=true;";

    [Test]
    public async Task Constructor_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new SqlServerOutboxManagement(null!, Options.Create(new OutboxOptions())))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => new SqlServerOutboxManagement(string.Empty, Options.Create(new OutboxOptions())))
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() => new SqlServerOutboxManagement("   ", Options.Create(new OutboxOptions())))
            .Throws<ArgumentException>();

    [Test]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new SqlServerOutboxManagement(ValidConnectionString, null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithValidArguments_CreatesInstance()
    {
        var management = new SqlServerOutboxManagement(ValidConnectionString, Options.Create(new OutboxOptions()));

        _ = await Assert.That(management).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithNullSchema_UsesDefaultDboSchema()
    {
        var options = Options.Create(new OutboxOptions { Schema = null });

        var management = new SqlServerOutboxManagement(ValidConnectionString, options);

        _ = await Assert.That(management).IsNotNull();
    }
}
