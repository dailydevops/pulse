namespace NetEvolve.Pulse.Tests.Unit.PostgreSql;

using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("PostgreSql")]
public sealed class PostgreSqlOutboxTransactionScopeTests
{
    [Test]
    public async Task Constructor_WithDefaultParameter_CreatesInstance()
    {
        var scope = new PostgreSqlOutboxTransactionScope();

        _ = await Assert.That(scope).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithNullTransaction_CreatesInstance()
    {
        var scope = new PostgreSqlOutboxTransactionScope(null);

        _ = await Assert.That(scope).IsNotNull();
    }

    [Test]
    public async Task GetCurrentTransaction_WithDefaultParameter_ReturnsNull()
    {
        var scope = new PostgreSqlOutboxTransactionScope();

        var result = scope.GetCurrentTransaction();

        _ = await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetCurrentTransaction_WithNullTransaction_ReturnsNull()
    {
        var scope = new PostgreSqlOutboxTransactionScope(null);

        var result = scope.GetCurrentTransaction();

        _ = await Assert.That(result).IsNull();
    }
}
