namespace NetEvolve.Pulse.Tests.Unit.SQLite;

using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using static NetEvolve.Pulse.Outbox.OutboxOptionsExtensions;
using OutboxOptions = Pulse.Outbox.OutboxOptions;

[TestGroup("SQLite")]
public sealed class OutboxOptionsExtensionsTests
{
    [Test]
    public async Task FullTableName_WithDefaultOptions_Returns_quoted_table_name(CancellationToken cancellationToken)
    {
        var options = new OutboxOptions();

        _ = await Assert.That(options.FullTableName).IsEqualTo("\"OutboxMessage\"");
    }

    [Test]
    public async Task FullTableName_WithCustomTableName_Returns_quoted_table_name(CancellationToken cancellationToken)
    {
        var options = new OutboxOptions { TableName = "MyCustomTable" };

        _ = await Assert.That(options.FullTableName).IsEqualTo("\"MyCustomTable\"");
    }
}
