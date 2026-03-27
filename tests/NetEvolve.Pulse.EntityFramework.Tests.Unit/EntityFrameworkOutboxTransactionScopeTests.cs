namespace NetEvolve.Pulse.EntityFramework.Tests.Unit;

using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse;
using TUnit.Core;

public sealed class EntityFrameworkOutboxTransactionScopeTests
{
    [Test]
    public async Task Constructor_WithNullContext_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new EntityFrameworkOutboxTransactionScope<TestDbContext>(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithValidContext_CreatesInstance()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(Constructor_WithValidContext_CreatesInstance))
            .Options;
        await using var context = new TestDbContext(options);

        var scope = new EntityFrameworkOutboxTransactionScope<TestDbContext>(context);

        _ = await Assert.That(scope).IsNotNull();
    }

    [Test]
    public async Task GetCurrentTransaction_WithNoActiveTransaction_ReturnsNull()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(GetCurrentTransaction_WithNoActiveTransaction_ReturnsNull))
            .Options;
        await using var context = new TestDbContext(options);
        var scope = new EntityFrameworkOutboxTransactionScope<TestDbContext>(context);

        var result = scope.GetCurrentTransaction();

        _ = await Assert.That(result).IsNull();
    }
}
