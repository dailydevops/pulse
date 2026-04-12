namespace NetEvolve.Pulse.Tests.Unit.PostgreSql;

using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using static NetEvolve.Pulse.Outbox.PostgreSqlOutboxOptionsExtensions;
using OutboxOptions = Pulse.Outbox.OutboxOptions;

[TestGroup("PostgreSql")]
public sealed class PostgreSqlOutboxOptionsExtensionsTests
{
    [Test]
    public async Task FullTableName_WithDefaultOptions_Returns_correct_quoted_name(CancellationToken cancellationToken)
    {
        var options = new OutboxOptions();

        _ = await Assert.That(options.FullTableName).IsEqualTo("\"pulse\".\"OutboxMessage\"");
    }

    [Test]
    public async Task FullTableName_WithCustomSchema_Returns_correct_quoted_name(CancellationToken cancellationToken)
    {
        var options = new OutboxOptions { Schema = "myschema" };

        _ = await Assert.That(options.FullTableName).IsEqualTo("\"myschema\".\"OutboxMessage\"");
    }

    [Test]
    public async Task FullTableName_WithNullSchema_Falls_back_to_default_schema(CancellationToken cancellationToken)
    {
        var options = new OutboxOptions { Schema = null! };

        _ = await Assert.That(options.FullTableName).IsEqualTo("\"pulse\".\"OutboxMessage\"");
    }

    [Test]
    public async Task FullTableName_WithWhitespaceSchema_Falls_back_to_default_schema(
        CancellationToken cancellationToken
    )
    {
        var options = new OutboxOptions { Schema = "   " };

        _ = await Assert.That(options.FullTableName).IsEqualTo("\"pulse\".\"OutboxMessage\"");
    }

    [Test]
    public async Task FullTableName_WithCustomTableName_Returns_correct_quoted_name(CancellationToken cancellationToken)
    {
        var options = new OutboxOptions { TableName = "MyTable" };

        _ = await Assert.That(options.FullTableName).IsEqualTo("\"pulse\".\"MyTable\"");
    }

    [Test]
    public async Task FullTableName_Trims_schema_whitespace(CancellationToken cancellationToken)
    {
        var options = new OutboxOptions { Schema = "  myschema  " };

        _ = await Assert.That(options.FullTableName).IsEqualTo("\"myschema\".\"OutboxMessage\"");
    }
}
