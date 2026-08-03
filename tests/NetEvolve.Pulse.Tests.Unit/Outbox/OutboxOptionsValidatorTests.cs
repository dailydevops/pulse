namespace NetEvolve.Pulse.Tests.Unit.Outbox;

using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("Outbox")]
public class OutboxOptionsValidatorTests
{
    private static readonly OutboxOptionsValidator _validator = new();

    [Test]
    public async Task Validate_DefaultOptions_Succeeds()
    {
        var options = new OutboxOptions();

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_WithNullTableName_Fails()
    {
        var options = new OutboxOptions { TableName = null! };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_WithEmptyTableName_Fails()
    {
        var options = new OutboxOptions { TableName = string.Empty };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_WithWhitespaceTableName_Fails()
    {
        var options = new OutboxOptions { TableName = "   " };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Validate_WithValidTableName_Succeeds()
    {
        var options = new OutboxOptions { TableName = "CustomOutboxMessage" };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_WithNullConnectionString_Succeeds()
    {
        // ConnectionString is legitimately null for EF Core-based outbox usage; it must not be
        // treated as invalid by this validator.
        var options = new OutboxOptions { ConnectionString = null };

        var result = _validator.Validate(null, options);

        _ = await Assert.That(result.Succeeded).IsTrue();
    }
}
