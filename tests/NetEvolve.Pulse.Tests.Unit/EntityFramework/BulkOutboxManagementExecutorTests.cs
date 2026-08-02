namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("EntityFramework")]
public sealed class BulkOutboxManagementExecutorTests
{
    [Test]
    public async Task GetDeadLetterMessages_ReturnsOnlyDeadLetterRows_OrderedAndPaged(
        CancellationToken cancellationToken
    )
    {
        var connectionString =
            "Data Source=BulkOutboxMgmtGetDeadLetterMessages;Mode=Memory;Cache=Shared;Foreign Keys=False;Pooling=False";

        var keeperConnection = new SqliteConnection(connectionString);
        await using (keeperConnection.ConfigureAwait(false))
        {
            await keeperConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var options = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connectionString).Options;
            var context = new TestDbContext(options);
            await using (context.ConfigureAwait(false))
            {
                _ = await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

                var baseTime = DateTimeOffset.UtcNow.AddMinutes(-30);

                var deadLetter1 = CreateMessage(OutboxMessageStatus.DeadLetter, baseTime.AddMinutes(1));
                var deadLetter2 = CreateMessage(OutboxMessageStatus.DeadLetter, baseTime.AddMinutes(2));
                var deadLetter3 = CreateMessage(OutboxMessageStatus.DeadLetter, baseTime.AddMinutes(3));
                var pending = CreateMessage(OutboxMessageStatus.Pending, baseTime.AddMinutes(4));

                await context
                    .OutboxMessages.AddRangeAsync([deadLetter1, deadLetter2, deadLetter3, pending], cancellationToken)
                    .ConfigureAwait(false);
                _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                context.ChangeTracker.Clear();

                var executor = new BulkOutboxManagementExecutor<TestDbContext>(context);

                var allDeadLetter = await executor
                    .GetDeadLetterMessages(0, 10)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var paged = await executor
                    .GetDeadLetterMessages(1, 1)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                using (Assert.Multiple())
                {
                    _ = await Assert.That(allDeadLetter).HasCount().EqualTo(3);
                    _ = await Assert.That(allDeadLetter[0].Id).IsEqualTo(deadLetter3.Id);
                    _ = await Assert.That(allDeadLetter[1].Id).IsEqualTo(deadLetter2.Id);
                    _ = await Assert.That(allDeadLetter[2].Id).IsEqualTo(deadLetter1.Id);
                    _ = await Assert.That(paged).HasCount().EqualTo(1);
                    _ = await Assert.That(paged[0].Id).IsEqualTo(deadLetter2.Id);
                }
            }
        }
    }

    [Test]
    public async Task GetDeadLetterMessageAsync_WhenDeadLetter_ReturnsMessage(CancellationToken cancellationToken)
    {
        var connectionString =
            "Data Source=BulkOutboxMgmtGetDeadLetterMessageFound;Mode=Memory;Cache=Shared;Foreign Keys=False;Pooling=False";

        var keeperConnection = new SqliteConnection(connectionString);
        await using (keeperConnection.ConfigureAwait(false))
        {
            await keeperConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var options = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connectionString).Options;
            var context = new TestDbContext(options);
            await using (context.ConfigureAwait(false))
            {
                _ = await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

                var message = CreateMessage(OutboxMessageStatus.DeadLetter, DateTimeOffset.UtcNow);
                _ = await context.OutboxMessages.AddAsync(message, cancellationToken).ConfigureAwait(false);
                _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                context.ChangeTracker.Clear();

                var executor = new BulkOutboxManagementExecutor<TestDbContext>(context);

                var result = await executor
                    .GetDeadLetterMessageAsync(message.Id, cancellationToken)
                    .ConfigureAwait(false);

                using (Assert.Multiple())
                {
                    _ = await Assert.That(result).IsNotNull();
                    _ = await Assert.That(result!.Id).IsEqualTo(message.Id);
                    _ = await Assert.That(result.Status).IsEqualTo(OutboxMessageStatus.DeadLetter);
                }
            }
        }
    }

    [Test]
    public async Task GetDeadLetterMessageAsync_WhenIdDoesNotExist_ReturnsNull(CancellationToken cancellationToken)
    {
        var connectionString =
            "Data Source=BulkOutboxMgmtGetDeadLetterMessageMissing;Mode=Memory;Cache=Shared;Foreign Keys=False;Pooling=False";

        var keeperConnection = new SqliteConnection(connectionString);
        await using (keeperConnection.ConfigureAwait(false))
        {
            await keeperConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var options = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connectionString).Options;
            var context = new TestDbContext(options);
            await using (context.ConfigureAwait(false))
            {
                _ = await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

                var executor = new BulkOutboxManagementExecutor<TestDbContext>(context);

                var result = await executor
                    .GetDeadLetterMessageAsync(Guid.NewGuid(), cancellationToken)
                    .ConfigureAwait(false);

                _ = await Assert.That(result).IsNull();
            }
        }
    }

    [Test]
    public async Task GetDeadLetterMessageAsync_WhenMessageNotDeadLetter_ReturnsNull(
        CancellationToken cancellationToken
    )
    {
        var connectionString =
            "Data Source=BulkOutboxMgmtGetDeadLetterMessageWrongStatus;Mode=Memory;Cache=Shared;Foreign Keys=False;Pooling=False";

        var keeperConnection = new SqliteConnection(connectionString);
        await using (keeperConnection.ConfigureAwait(false))
        {
            await keeperConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var options = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connectionString).Options;
            var context = new TestDbContext(options);
            await using (context.ConfigureAwait(false))
            {
                _ = await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

                var message = CreateMessage(OutboxMessageStatus.Pending, DateTimeOffset.UtcNow);
                _ = await context.OutboxMessages.AddAsync(message, cancellationToken).ConfigureAwait(false);
                _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                context.ChangeTracker.Clear();

                var executor = new BulkOutboxManagementExecutor<TestDbContext>(context);

                var result = await executor
                    .GetDeadLetterMessageAsync(message.Id, cancellationToken)
                    .ConfigureAwait(false);

                _ = await Assert.That(result).IsNull();
            }
        }
    }

    [Test]
    public async Task ReplayByIdAsync_WhenDeadLetter_ResetsMessageAndReturnsTrue(CancellationToken cancellationToken)
    {
        var connectionString =
            "Data Source=BulkOutboxMgmtReplayByIdFound;Mode=Memory;Cache=Shared;Foreign Keys=False;Pooling=False";

        var keeperConnection = new SqliteConnection(connectionString);
        await using (keeperConnection.ConfigureAwait(false))
        {
            await keeperConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var options = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connectionString).Options;
            var context = new TestDbContext(options);
            await using (context.ConfigureAwait(false))
            {
                _ = await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

                var message = CreateMessage(OutboxMessageStatus.DeadLetter, DateTimeOffset.UtcNow.AddMinutes(-10));
                message.RetryCount = 5;
                message.Error = "boom";
                _ = await context.OutboxMessages.AddAsync(message, cancellationToken).ConfigureAwait(false);
                _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                context.ChangeTracker.Clear();

                var executor = new BulkOutboxManagementExecutor<TestDbContext>(context);

                var updatedAt = DateTimeOffset.UtcNow;
                var replayed = await executor
                    .ReplayByIdAsync(message.Id, updatedAt, cancellationToken)
                    .ConfigureAwait(false);

                var stored = await context
                    .OutboxMessages.AsNoTracking()
                    .SingleAsync(m => m.Id == message.Id, cancellationToken)
                    .ConfigureAwait(false);

                using (Assert.Multiple())
                {
                    _ = await Assert.That(replayed).IsTrue();
                    _ = await Assert.That(stored.Status).IsEqualTo(OutboxMessageStatus.Pending);
                    _ = await Assert.That(stored.RetryCount).IsEqualTo(0);
                    _ = await Assert.That(stored.Error).IsNull();
                    _ = await Assert.That(stored.UpdatedAt).IsEqualTo(updatedAt);
                }
            }
        }
    }

    [Test]
    public async Task ReplayByIdAsync_WhenIdDoesNotExist_ReturnsFalse(CancellationToken cancellationToken)
    {
        var connectionString =
            "Data Source=BulkOutboxMgmtReplayByIdMissing;Mode=Memory;Cache=Shared;Foreign Keys=False;Pooling=False";

        var keeperConnection = new SqliteConnection(connectionString);
        await using (keeperConnection.ConfigureAwait(false))
        {
            await keeperConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var options = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connectionString).Options;
            var context = new TestDbContext(options);
            await using (context.ConfigureAwait(false))
            {
                _ = await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

                var executor = new BulkOutboxManagementExecutor<TestDbContext>(context);

                var replayed = await executor
                    .ReplayByIdAsync(Guid.NewGuid(), DateTimeOffset.UtcNow, cancellationToken)
                    .ConfigureAwait(false);

                _ = await Assert.That(replayed).IsFalse();
            }
        }
    }

    [Test]
    public async Task ReplayByIdAsync_WhenMessageNotDeadLetter_ReturnsFalseAndDoesNotChange(
        CancellationToken cancellationToken
    )
    {
        var connectionString =
            "Data Source=BulkOutboxMgmtReplayByIdWrongStatus;Mode=Memory;Cache=Shared;Foreign Keys=False;Pooling=False";

        var keeperConnection = new SqliteConnection(connectionString);
        await using (keeperConnection.ConfigureAwait(false))
        {
            await keeperConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var options = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connectionString).Options;
            var context = new TestDbContext(options);
            await using (context.ConfigureAwait(false))
            {
                _ = await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

                var message = CreateMessage(OutboxMessageStatus.Completed, DateTimeOffset.UtcNow.AddMinutes(-10));
                message.RetryCount = 2;
                _ = await context.OutboxMessages.AddAsync(message, cancellationToken).ConfigureAwait(false);
                _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                context.ChangeTracker.Clear();

                var executor = new BulkOutboxManagementExecutor<TestDbContext>(context);

                var replayed = await executor
                    .ReplayByIdAsync(message.Id, DateTimeOffset.UtcNow, cancellationToken)
                    .ConfigureAwait(false);

                var stored = await context
                    .OutboxMessages.AsNoTracking()
                    .SingleAsync(m => m.Id == message.Id, cancellationToken)
                    .ConfigureAwait(false);

                using (Assert.Multiple())
                {
                    _ = await Assert.That(replayed).IsFalse();
                    _ = await Assert.That(stored.Status).IsEqualTo(OutboxMessageStatus.Completed);
                    _ = await Assert.That(stored.RetryCount).IsEqualTo(2);
                }
            }
        }
    }

    [Test]
    public async Task ReplayAllAsync_ResetsAllDeadLetterMessages_AndReturnsCount(CancellationToken cancellationToken)
    {
        var connectionString =
            "Data Source=BulkOutboxMgmtReplayAllFound;Mode=Memory;Cache=Shared;Foreign Keys=False;Pooling=False";

        var keeperConnection = new SqliteConnection(connectionString);
        await using (keeperConnection.ConfigureAwait(false))
        {
            await keeperConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var options = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connectionString).Options;
            var context = new TestDbContext(options);
            await using (context.ConfigureAwait(false))
            {
                _ = await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

                var deadLetter1 = CreateMessage(OutboxMessageStatus.DeadLetter, DateTimeOffset.UtcNow.AddMinutes(-10));
                deadLetter1.RetryCount = 3;
                deadLetter1.Error = "err1";
                var deadLetter2 = CreateMessage(OutboxMessageStatus.DeadLetter, DateTimeOffset.UtcNow.AddMinutes(-5));
                deadLetter2.RetryCount = 7;
                deadLetter2.Error = "err2";
                var pending = CreateMessage(OutboxMessageStatus.Pending, DateTimeOffset.UtcNow);

                await context
                    .OutboxMessages.AddRangeAsync([deadLetter1, deadLetter2, pending], cancellationToken)
                    .ConfigureAwait(false);
                _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                context.ChangeTracker.Clear();

                var executor = new BulkOutboxManagementExecutor<TestDbContext>(context);

                var updatedAt = DateTimeOffset.UtcNow;
                var replayedCount = await executor.ReplayAllAsync(updatedAt, cancellationToken).ConfigureAwait(false);

                var stored = await context
                    .OutboxMessages.AsNoTracking()
                    .OrderBy(m => m.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var storedDeadLetter1 = stored.Single(m => m.Id == deadLetter1.Id);
                var storedDeadLetter2 = stored.Single(m => m.Id == deadLetter2.Id);
                var storedPending = stored.Single(m => m.Id == pending.Id);

                using (Assert.Multiple())
                {
                    _ = await Assert.That(replayedCount).IsEqualTo(2);

                    _ = await Assert.That(storedDeadLetter1.Status).IsEqualTo(OutboxMessageStatus.Pending);
                    _ = await Assert.That(storedDeadLetter1.RetryCount).IsEqualTo(0);
                    _ = await Assert.That(storedDeadLetter1.Error).IsNull();
                    _ = await Assert.That(storedDeadLetter1.UpdatedAt).IsEqualTo(updatedAt);

                    _ = await Assert.That(storedDeadLetter2.Status).IsEqualTo(OutboxMessageStatus.Pending);
                    _ = await Assert.That(storedDeadLetter2.RetryCount).IsEqualTo(0);
                    _ = await Assert.That(storedDeadLetter2.Error).IsNull();
                    _ = await Assert.That(storedDeadLetter2.UpdatedAt).IsEqualTo(updatedAt);

                    _ = await Assert.That(storedPending.Status).IsEqualTo(OutboxMessageStatus.Pending);
                    _ = await Assert.That(storedPending.UpdatedAt).IsNotEqualTo(updatedAt);
                }
            }
        }
    }

    [Test]
    public async Task ReplayAllAsync_WhenNoDeadLetterMessages_ReturnsZero(CancellationToken cancellationToken)
    {
        var connectionString =
            "Data Source=BulkOutboxMgmtReplayAllNone;Mode=Memory;Cache=Shared;Foreign Keys=False;Pooling=False";

        var keeperConnection = new SqliteConnection(connectionString);
        await using (keeperConnection.ConfigureAwait(false))
        {
            await keeperConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var options = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connectionString).Options;
            var context = new TestDbContext(options);
            await using (context.ConfigureAwait(false))
            {
                _ = await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

                var pending = CreateMessage(OutboxMessageStatus.Pending, DateTimeOffset.UtcNow);
                _ = await context.OutboxMessages.AddAsync(pending, cancellationToken).ConfigureAwait(false);
                _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                context.ChangeTracker.Clear();

                var executor = new BulkOutboxManagementExecutor<TestDbContext>(context);

                var replayedCount = await executor
                    .ReplayAllAsync(DateTimeOffset.UtcNow, cancellationToken)
                    .ConfigureAwait(false);

                _ = await Assert.That(replayedCount).IsEqualTo(0);
            }
        }
    }

    private static OutboxMessage CreateMessage(OutboxMessageStatus status, DateTimeOffset updatedAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            EventType = typeof(string),
            Payload = "{}",
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt,
            Status = status,
        };
}
