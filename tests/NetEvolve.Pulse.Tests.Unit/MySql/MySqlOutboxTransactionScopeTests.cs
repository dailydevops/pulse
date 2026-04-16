namespace NetEvolve.Pulse.Tests.Unit.MySql;

using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("MySql")]
public sealed class MySqlOutboxTransactionScopeTests
{
    [Test]
    public async Task Constructor_WithDefaultParameter_CreatesInstance()
    {
        var scope = new MySqlOutboxTransactionScope();

        _ = await Assert.That(scope).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithNullTransaction_CreatesInstance()
    {
        var scope = new MySqlOutboxTransactionScope(null);

        _ = await Assert.That(scope).IsNotNull();
    }

    [Test]
    public async Task GetCurrentTransaction_WithDefaultParameter_ReturnsNull()
    {
        var scope = new MySqlOutboxTransactionScope();

        var result = scope.GetCurrentTransaction();

        _ = await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetCurrentTransaction_WithNullTransaction_ReturnsNull()
    {
        var scope = new MySqlOutboxTransactionScope(null);

        var result = scope.GetCurrentTransaction();

        _ = await Assert.That(result).IsNull();
    }
}
