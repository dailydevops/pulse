namespace NetEvolve.Pulse.Tests.Unit.SQLite;

using System.Threading.Tasks;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using static global::NetEvolve.Pulse.Outbox.OutboxOptionsExtensions;
using OutboxOptions = global::NetEvolve.Pulse.Outbox.OutboxOptions;

public sealed class OutboxOptionsExtensionsTests
{
    [Test]
    public async Task FullTableName_WithDefaultOptions_Returns_quoted_table_name()
    {
        var options = new OutboxOptions();

        _ = await Assert.That(options.FullTableName).IsEqualTo("\"OutboxMessage\"");
    }

    [Test]
    public async Task FullTableName_WithCustomTableName_Returns_quoted_table_name()
    {
        var options = new OutboxOptions { TableName = "MyCustomTable" };

        _ = await Assert.That(options.FullTableName).IsEqualTo("\"MyCustomTable\"");
    }
}
