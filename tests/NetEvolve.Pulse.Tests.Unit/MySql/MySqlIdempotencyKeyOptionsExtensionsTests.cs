namespace NetEvolve.Pulse.Tests.Unit.MySql;

using System;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using static NetEvolve.Pulse.Idempotency.MySqlIdempotencyKeyOptionsExtensions;
using IdempotencyKeyOptions = Pulse.Idempotency.IdempotencyKeyOptions;

[TestGroup("MySql")]
public sealed class MySqlIdempotencyKeyOptionsExtensionsTests
{
    [Test]
    public async Task FullTableName_WithDefaultOptions_Returns_correct_backtick_quoted_name()
    {
        var options = new IdempotencyKeyOptions();

        _ = await Assert.That(options.FullTableName).IsEqualTo("`IdempotencyKey`");
    }

    [Test]
    public async Task FullTableName_WithCustomTableName_Returns_correct_backtick_quoted_name()
    {
        var options = new IdempotencyKeyOptions { TableName = "MyTable" };

        _ = await Assert.That(options.FullTableName).IsEqualTo("`MyTable`");
    }

    [Test]
    public async Task FullTableName_DoesNotUseSchema_IgnoresSchemaProperty()
    {
        var options = new IdempotencyKeyOptions { Schema = "myschema", TableName = "IdempotencyKey" };

        _ = await Assert.That(options.FullTableName).IsEqualTo("`IdempotencyKey`");
    }

    [Test]
    public async Task FullTableName_WithInvalidTableName_ThrowsArgumentException()
    {
        var options = new IdempotencyKeyOptions { TableName = "with`backtick" };

        _ = await Assert.That(() => options.FullTableName).Throws<ArgumentException>();
    }
}
