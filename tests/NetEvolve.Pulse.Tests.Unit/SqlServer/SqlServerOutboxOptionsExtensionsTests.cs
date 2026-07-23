namespace NetEvolve.Pulse.Tests.Unit.SqlServer;

using System;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using static NetEvolve.Pulse.Outbox.SqlServerOutboxOptionsExtensions;
using OutboxOptions = Pulse.Outbox.OutboxOptions;

[TestGroup("SqlServer")]
public sealed class SqlServerOutboxOptionsExtensionsTests
{
    [Test]
    public async Task FullTableName_WithDefaultOptions_Returns_correct_bracketed_name()
    {
        var options = new OutboxOptions();

        _ = await Assert.That(options.FullTableName).IsEqualTo("[pulse].[OutboxMessage]");
    }

    [Test]
    public async Task FullTableName_WithCustomSchema_Returns_correct_bracketed_name()
    {
        var options = new OutboxOptions { Schema = "myschema" };

        _ = await Assert.That(options.FullTableName).IsEqualTo("[myschema].[OutboxMessage]");
    }

    [Test]
    public async Task FullTableName_WithNullSchema_Falls_back_to_default_schema()
    {
        var options = new OutboxOptions { Schema = null! };

        _ = await Assert.That(options.FullTableName).IsEqualTo("[pulse].[OutboxMessage]");
    }

    [Test]
    public async Task FullTableName_WithWhitespaceSchema_Falls_back_to_default_schema()
    {
        var options = new OutboxOptions { Schema = "   " };

        _ = await Assert.That(options.FullTableName).IsEqualTo("[pulse].[OutboxMessage]");
    }

    [Test]
    public async Task FullTableName_WithCustomTableName_Returns_correct_bracketed_name()
    {
        var options = new OutboxOptions { TableName = "MyTable" };

        _ = await Assert.That(options.FullTableName).IsEqualTo("[pulse].[MyTable]");
    }

    [Test]
    public async Task FullTableName_Trims_schema_whitespace()
    {
        var options = new OutboxOptions { Schema = "  myschema  " };

        _ = await Assert.That(options.FullTableName).IsEqualTo("[myschema].[OutboxMessage]");
    }

    // Defense-in-depth: prevent SQL injection through configuration-supplied Schema/TableName
    // that bypasses the [bracketed] identifier by including a closing bracket and arbitrary SQL.
    // See SqlIdentifierTests for the underlying allowlist contract.

    [Test]
    public async Task FullTableName_RejectsBracketBreakoutInSchema() =>
        _ = await Assert
            .That(() => _ = new OutboxOptions { Schema = "pulse].[evil] -- " }.FullTableName)
            .Throws<ArgumentException>();

    [Test]
    public async Task FullTableName_RejectsBracketBreakoutInTableName() =>
        _ = await Assert
            .That(() => _ = new OutboxOptions { TableName = "OutboxMessage]; DROP TABLE Users; --" }.FullTableName)
            .Throws<ArgumentException>();

    [Test]
    public async Task FullTableName_RejectsSemicolonInTableName() =>
        _ = await Assert
            .That(() => _ = new OutboxOptions { TableName = "x;y" }.FullTableName)
            .Throws<ArgumentException>();
}
