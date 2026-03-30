namespace NetEvolve.Pulse.SQLite.Tests.Unit;

using System.Threading.Tasks;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

public sealed class SQLiteOutboxTransactionScopeTests
{
    [Test]
    public async Task Constructor_WithDefaultParameter_CreatesInstance()
    {
        var scope = new SQLiteOutboxTransactionScope();

        _ = await Assert.That(scope).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithNullTransaction_CreatesInstance()
    {
        var scope = new SQLiteOutboxTransactionScope(null);

        _ = await Assert.That(scope).IsNotNull();
    }

    [Test]
    public async Task GetCurrentTransaction_WithDefaultParameter_ReturnsNull()
    {
        var scope = new SQLiteOutboxTransactionScope();

        var result = scope.GetCurrentTransaction();

        _ = await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetCurrentTransaction_WithNullTransaction_ReturnsNull()
    {
        var scope = new SQLiteOutboxTransactionScope(null);

        var result = scope.GetCurrentTransaction();

        _ = await Assert.That(result).IsNull();
    }
}
