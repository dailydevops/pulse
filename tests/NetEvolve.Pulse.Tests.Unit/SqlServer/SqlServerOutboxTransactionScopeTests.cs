namespace NetEvolve.Pulse.Tests.Unit.SqlServer;

using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("SqlServer")]
public sealed class SqlServerOutboxTransactionScopeTests
{
    [Test]
    public async Task Constructor_WithDefaultParameter_CreatesInstance(CancellationToken cancellationToken)
    {
        var scope = new SqlServerOutboxTransactionScope();

        _ = await Assert.That(scope).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithNullTransaction_CreatesInstance(CancellationToken cancellationToken)
    {
        var scope = new SqlServerOutboxTransactionScope(null);

        _ = await Assert.That(scope).IsNotNull();
    }

    [Test]
    public async Task GetCurrentTransaction_WithDefaultParameter_ReturnsNull(CancellationToken cancellationToken)
    {
        var scope = new SqlServerOutboxTransactionScope();

        var result = scope.GetCurrentTransaction();

        _ = await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetCurrentTransaction_WithNullTransaction_ReturnsNull(CancellationToken cancellationToken)
    {
        var scope = new SqlServerOutboxTransactionScope(null);

        var result = scope.GetCurrentTransaction();

        _ = await Assert.That(result).IsNull();
    }
}
