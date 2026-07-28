namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("EntityFramework")]
public sealed class TrackingOutboxRepositoryExecutorSemaphoreTests
{
    [Test]
    public async Task FetchAndMarkAsync_WhileSemaphoreHeld_WaitsForRelease(CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(FetchAndMarkAsync_WhileSemaphoreHeld_WaitsForRelease))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            _ = await SeedMessageAsync(context, OutboxMessageStatus.Pending, cancellationToken).ConfigureAwait(false);

            using var executor = new InMemoryOutboxRepositoryExecutor<TestDbContext>(context, 1);
            var semaphore = GetSemaphore(executor);

            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            var operation = executor.FetchAndMarkAsync(
                context.OutboxMessages.Where(m => m.Status == OutboxMessageStatus.Pending),
                DateTimeOffset.UtcNow,
                OutboxMessageStatus.Processing,
                cancellationToken
            );

            var completedWhileHeld = await Task.WhenAny(operation, Task.Delay(500, cancellationToken))
                .ConfigureAwait(false);
            _ = await Assert.That(completedWhileHeld).IsNotEqualTo((Task)operation);

            _ = semaphore.Release();

            var result = await operation.ConfigureAwait(false);
            _ = await Assert.That(result).HasCount().EqualTo(1);
        }
    }

    [Test]
    public async Task DeleteByQueryAsync_WhileSemaphoreHeld_WaitsForRelease(CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(DeleteByQueryAsync_WhileSemaphoreHeld_WaitsForRelease))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            _ = await SeedMessageAsync(context, OutboxMessageStatus.Completed, cancellationToken).ConfigureAwait(false);

            using var executor = new InMemoryOutboxRepositoryExecutor<TestDbContext>(context, 1);
            var semaphore = GetSemaphore(executor);

            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            var operation = executor.DeleteByQueryAsync(
                context.OutboxMessages.Where(m => m.Status == OutboxMessageStatus.Completed),
                cancellationToken
            );

            var completedWhileHeld = await Task.WhenAny(operation, Task.Delay(500, cancellationToken))
                .ConfigureAwait(false);
            _ = await Assert.That(completedWhileHeld).IsNotEqualTo((Task)operation);

            _ = semaphore.Release();

            var deleted = await operation.ConfigureAwait(false);
            _ = await Assert.That(deleted).IsEqualTo(1);
        }
    }

    [Test]
    public async Task UpdateByIdsAsync_MySqlExecutor_WhileSemaphoreHeld_WaitsForRelease(
        CancellationToken cancellationToken
    )
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(UpdateByIdsAsync_MySqlExecutor_WhileSemaphoreHeld_WaitsForRelease))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var messageId = await SeedMessageAsync(context, OutboxMessageStatus.Processing, cancellationToken)
                .ConfigureAwait(false);

            using var executor = new MySqlOutboxRepositoryExecutor<TestDbContext>(context, 1);
            var semaphore = GetSemaphore(executor);

            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            var operation = executor.UpdateByIdsAsync(
                [messageId],
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null,
                OutboxMessageStatus.Completed,
                0,
                null,
                cancellationToken
            );

            var completedWhileHeld = await Task.WhenAny(operation, Task.Delay(500, cancellationToken))
                .ConfigureAwait(false);
            _ = await Assert.That(completedWhileHeld).IsNotEqualTo(operation);

            _ = semaphore.Release();

            await operation.ConfigureAwait(false);

            var message = await context
                .OutboxMessages.SingleAsync(m => m.Id == messageId, cancellationToken)
                .ConfigureAwait(false);
            _ = await Assert.That(message.Status).IsEqualTo(OutboxMessageStatus.Completed);
        }
    }

    private static async Task<Guid> SeedMessageAsync(
        TestDbContext context,
        OutboxMessageStatus status,
        CancellationToken cancellationToken
    )
    {
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = typeof(string),
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            ProcessedAt = status == OutboxMessageStatus.Completed ? DateTimeOffset.UtcNow.AddMinutes(-10) : null,
            Status = status,
        };
        _ = await context.OutboxMessages.AddAsync(message, cancellationToken).ConfigureAwait(false);
        _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.ChangeTracker.Clear();
        return message.Id;
    }

    private static SemaphoreSlim GetSemaphore(object executor) =>
        (SemaphoreSlim)
            typeof(TrackingOutboxRepositoryExecutorBase<TestDbContext>)
                .GetField("_semaphore", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(executor)!;
}
