namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("EntityFramework")]
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
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var scope = new EntityFrameworkOutboxTransactionScope<TestDbContext>(context);

            _ = await Assert.That(scope).IsNotNull();
        }
    }

    [Test]
    public async Task GetCurrentTransaction_WithNoActiveTransaction_ReturnsNull()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(GetCurrentTransaction_WithNoActiveTransaction_ReturnsNull))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var scope = new EntityFrameworkOutboxTransactionScope<TestDbContext>(context);

            var result = scope.GetCurrentTransaction();

            _ = await Assert.That(result).IsNull();
        }
    }
}
