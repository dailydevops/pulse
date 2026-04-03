namespace NetEvolve.Pulse.Tests.Unit.SqlServer;

using System.Threading.Tasks;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using static global::NetEvolve.Pulse.Outbox.SqlServerOutboxOptionsExtensions;
using OutboxOptions = global::NetEvolve.Pulse.Outbox.OutboxOptions;

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
}
