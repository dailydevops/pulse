namespace NetEvolve.Pulse.Tests.Unit.MySql;

using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using static NetEvolve.Pulse.Outbox.MySqlOutboxOptionsExtensions;
using OutboxOptions = Pulse.Outbox.OutboxOptions;

[TestGroup("MySql")]
public sealed class MySqlOutboxOptionsExtensionsTests
{
    [Test]
    public async Task FullTableName_WithDefaultOptions_Returns_correct_backtick_quoted_name()
    {
        var options = new OutboxOptions();

        _ = await Assert.That(options.FullTableName).IsEqualTo("`OutboxMessage`");
    }

    [Test]
    public async Task FullTableName_WithCustomTableName_Returns_correct_backtick_quoted_name()
    {
        var options = new OutboxOptions { TableName = "MyTable" };

        _ = await Assert.That(options.FullTableName).IsEqualTo("`MyTable`");
    }

    [Test]
    public async Task FullTableName_DoesNotUseSchema_IgnoresSchemaProperty()
    {
        var options = new OutboxOptions { Schema = "myschema", TableName = "OutboxMessage" };

        _ = await Assert.That(options.FullTableName).IsEqualTo("`OutboxMessage`");
    }
}
