namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("EntityFramework")]
public sealed class BulkOutboxRepositoryExecutorTests
{
    [Test]
    public async Task Constructor_WithZeroMaxDegreeOfParallelism_CreatesInstance()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(Constructor_WithZeroMaxDegreeOfParallelism_CreatesInstance))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            using var executor = new BulkOutboxRepositoryExecutor<TestDbContext>(context, 0);

            _ = await Assert.That(executor).IsNotNull();
        }
    }

    [Test]
    public async Task FetchAndMarkAsync_WithZeroMaxDegreeOfParallelism_CompletesWithoutDeadlock(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(FetchAndMarkAsync_WithZeroMaxDegreeOfParallelism_CompletesWithoutDeadlock))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            using var executor = new BulkOutboxRepositoryExecutor<TestDbContext>(context, 0);

            var result = await executor
                .FetchAndMarkAsync(
                    context.OutboxMessages.Where(m => m.Status == OutboxMessageStatus.Pending),
                    DateTimeOffset.UtcNow,
                    OutboxMessageStatus.Processing,
                    cancellationToken
                )
                .ConfigureAwait(false);

            _ = await Assert.That(result).IsEmpty();
        }
    }

    [Test]
    public async Task Constructor_WithNegativeMaxDegreeOfParallelism_CreatesInstance()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(Constructor_WithNegativeMaxDegreeOfParallelism_CreatesInstance))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            using var executor = new BulkOutboxRepositoryExecutor<TestDbContext>(context, -1);

            _ = await Assert.That(executor).IsNotNull();
        }
    }
}
