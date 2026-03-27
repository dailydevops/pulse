namespace NetEvolve.Pulse.EntityFramework.Tests.Unit;

using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse;
using TUnit.Core;

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
        await using var context = new TestDbContext(options);

        _ = await Assert
            .That(() => new EntityFrameworkOutboxRepository<TestDbContext>(context, null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_WithValidArguments_CreatesInstance()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(Constructor_WithValidArguments_CreatesInstance))
            .Options;
        await using var context = new TestDbContext(options);

        var repository = new EntityFrameworkOutboxRepository<TestDbContext>(context, TimeProvider.System);

        _ = await Assert.That(repository).IsNotNull();
    }

    [Test]
    public async Task AddAsync_WithNullMessage_ThrowsArgumentNullException()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(AddAsync_WithNullMessage_ThrowsArgumentNullException))
            .Options;
        await using var context = new TestDbContext(options);
        var repository = new EntityFrameworkOutboxRepository<TestDbContext>(context, TimeProvider.System);

        _ = await Assert
            .That(async () => await repository.AddAsync(null!).ConfigureAwait(false))
            .Throws<ArgumentNullException>();
    }
}
