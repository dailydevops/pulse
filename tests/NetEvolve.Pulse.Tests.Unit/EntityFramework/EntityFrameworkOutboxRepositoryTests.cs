namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("EntityFramework")]
public sealed class EntityFrameworkOutboxRepositoryTests
{
    [Test]
    public async Task Constructor_WithNullContext_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new EntityFrameworkOutboxRepository<TestDbContext>(null!, TimeProvider.System))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(Constructor_WithNullTimeProvider_ThrowsArgumentNullException))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            _ = await Assert
                .That(() => new EntityFrameworkOutboxRepository<TestDbContext>(context, null!))
                .Throws<ArgumentNullException>();
        }
    }

    [Test]
    public async Task Constructor_WithValidArguments_CreatesInstance()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(Constructor_WithValidArguments_CreatesInstance))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            using var repository = new EntityFrameworkOutboxRepository<TestDbContext>(context, TimeProvider.System);

            _ = await Assert.That(repository).IsNotNull();
        }
    }

    [Test]
    public async Task AddAsync_WithNullMessage_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(AddAsync_WithNullMessage_ThrowsArgumentNullException))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            using var repository = new EntityFrameworkOutboxRepository<TestDbContext>(context, TimeProvider.System);

            _ = await Assert
                .That(async () => await repository.AddAsync(null!, cancellationToken).ConfigureAwait(false))
                .Throws<ArgumentNullException>();
        }
    }

    [Test]
    public async Task IsHealthyAsync_WithInMemoryProvider_ReturnsTrue(CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(IsHealthyAsync_WithInMemoryProvider_ReturnsTrue))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            using var repository = new EntityFrameworkOutboxRepository<TestDbContext>(context, TimeProvider.System);

            var result = await repository.IsHealthyAsync(cancellationToken).ConfigureAwait(false);

            _ = await Assert.That(result).IsTrue();
        }
    }
}
