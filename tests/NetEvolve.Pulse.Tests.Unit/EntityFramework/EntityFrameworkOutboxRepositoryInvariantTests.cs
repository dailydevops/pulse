namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

/// <summary>
/// Behavioral invariant tests for <see cref="EntityFrameworkOutboxRepository{TContext}"/>
/// using the EF Core InMemory provider as the backing store. These tests target the
/// state-machine guarantees and time-based filtering that the outbox processor relies on.
/// </summary>
[TestGroup("EntityFramework")]
public sealed class EntityFrameworkOutboxRepositoryInvariantTests
{
    private static TestDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(databaseName).Options;
        return new TestDbContext(options);
    }

    private static OutboxMessage CreateMessage(
        OutboxMessageStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset? nextRetryAt = null,
        DateTimeOffset? processedAt = null,
        int retryCount = 0
    ) =>
        new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = typeof(TestEvent),
            Payload = "{}",
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            NextRetryAt = nextRetryAt,
            ProcessedAt = processedAt,
            RetryCount = retryCount,
            Status = status,
        };

    private sealed record TestEvent;

    // INVARIANT (Q08): GetPendingAsync must NOT return a Pending message whose
    // NextRetryAt is in the future. Returning such a message would defeat the
    // exponential-backoff schedule.
    [Test]
    public async Task GetPendingAsync_Excludes_messages_with_future_NextRetryAt(CancellationToken cancellationToken)
    {
        var fakeTime = new FakeTimeProvider(
            DateTimeOffset.Parse("2025-01-01T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture)
        );
        var context = CreateContext(nameof(GetPendingAsync_Excludes_messages_with_future_NextRetryAt));
        await using (context.ConfigureAwait(false))
        {
            // One pending message without retry schedule - should be returned.
            var dueMessage = CreateMessage(OutboxMessageStatus.Pending, fakeTime.GetUtcNow().AddMinutes(-5));
            // One pending message with NextRetryAt 10 minutes in the future - must be filtered out.
            var notDueMessage = CreateMessage(
                OutboxMessageStatus.Pending,
                fakeTime.GetUtcNow().AddMinutes(-5),
                nextRetryAt: fakeTime.GetUtcNow().AddMinutes(10)
            );
            _ = await context.OutboxMessages.AddAsync(dueMessage, cancellationToken).ConfigureAwait(false);
            _ = await context.OutboxMessages.AddAsync(notDueMessage, cancellationToken).ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            using var repository = new EntityFrameworkOutboxRepository<TestDbContext>(context, fakeTime);

            var pending = await repository.GetPendingAsync(10, cancellationToken).ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(pending).HasCount(1);
                _ = await Assert.That(pending[0].Id).IsEqualTo(dueMessage.Id);
            }
        }
    }

    // INVARIANT (Q08): GetPendingAsync MUST return messages whose NextRetryAt is now-or-past,
    // matching the SQL `(NextRetryAt IS NULL OR NextRetryAt <= now)` clause used by every provider.
    [Test]
    public async Task GetPendingAsync_Includes_messages_with_past_NextRetryAt(CancellationToken cancellationToken)
    {
        var fakeTime = new FakeTimeProvider(
            DateTimeOffset.Parse("2025-01-01T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture)
        );
        var context = CreateContext(nameof(GetPendingAsync_Includes_messages_with_past_NextRetryAt));
        await using (context.ConfigureAwait(false))
        {
            var pastMessage = CreateMessage(
                OutboxMessageStatus.Pending,
                fakeTime.GetUtcNow().AddMinutes(-5),
                nextRetryAt: fakeTime.GetUtcNow().AddSeconds(-1)
            );
            _ = await context.OutboxMessages.AddAsync(pastMessage, cancellationToken).ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            using var repository = new EntityFrameworkOutboxRepository<TestDbContext>(context, fakeTime);

            var pending = await repository.GetPendingAsync(10, cancellationToken).ConfigureAwait(false);

            _ = await Assert.That(pending).HasCount(1);
        }
    }

    // INVARIANT (Q08): GetPendingAsync must atomically transition selected messages to
    // Processing — this is the "claim" half of the outbox processor's fetch-and-mark loop.
    // After the call returns, every claimed message must already be in Processing status
    // so a second worker cannot pick it up.
    [Test]
    public async Task GetPendingAsync_Transitions_claimed_messages_to_Processing(CancellationToken cancellationToken)
    {
        var fakeTime = new FakeTimeProvider(
            DateTimeOffset.Parse("2025-01-01T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture)
        );
        var context = CreateContext(nameof(GetPendingAsync_Transitions_claimed_messages_to_Processing));
        await using (context.ConfigureAwait(false))
        {
            var msg = CreateMessage(OutboxMessageStatus.Pending, fakeTime.GetUtcNow().AddMinutes(-5));
            _ = await context.OutboxMessages.AddAsync(msg, cancellationToken).ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            using var repository = new EntityFrameworkOutboxRepository<TestDbContext>(context, fakeTime);

            var claimed = await repository.GetPendingAsync(10, cancellationToken).ConfigureAwait(false);

            _ = await Assert.That(claimed).HasCount(1);
            _ = await Assert.That(claimed[0].Status).IsEqualTo(OutboxMessageStatus.Processing);

            // Verify the change is persisted - re-read directly.
            var reloaded = await context
                .OutboxMessages.SingleAsync(m => m.Id == msg.Id, cancellationToken)
                .ConfigureAwait(false);
            _ = await Assert.That(reloaded.Status).IsEqualTo(OutboxMessageStatus.Processing);
        }
    }

    // INVARIANT (Q09): MarkAsCompletedAsync(id) MUST only transition messages currently
    // in Processing state. It must not "rescue" a DeadLetter or downgrade a Completed back.
    // The EF query filters on `m.Id == messageId && m.Status == Processing`.
    [Test]
    public async Task MarkAsCompletedAsync_Does_not_change_DeadLetter_to_Completed(CancellationToken cancellationToken)
    {
        var fakeTime = new FakeTimeProvider(
            DateTimeOffset.Parse("2025-01-01T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture)
        );
        var context = CreateContext(nameof(MarkAsCompletedAsync_Does_not_change_DeadLetter_to_Completed));
        await using (context.ConfigureAwait(false))
        {
            var dlq = CreateMessage(OutboxMessageStatus.DeadLetter, fakeTime.GetUtcNow().AddMinutes(-10));
            _ = await context.OutboxMessages.AddAsync(dlq, cancellationToken).ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            using var repository = new EntityFrameworkOutboxRepository<TestDbContext>(context, fakeTime);

            await repository.MarkAsCompletedAsync(dlq.Id, cancellationToken).ConfigureAwait(false);

            var reloaded = await context
                .OutboxMessages.SingleAsync(m => m.Id == dlq.Id, cancellationToken)
                .ConfigureAwait(false);
            _ = await Assert.That(reloaded.Status).IsEqualTo(OutboxMessageStatus.DeadLetter);
        }
    }

    // INVARIANT (Q09): MarkAsFailedAsync(id) likewise must only act on Processing messages.
    [Test]
    public async Task MarkAsFailedAsync_Does_not_change_Completed_to_Failed(CancellationToken cancellationToken)
    {
        var fakeTime = new FakeTimeProvider(
            DateTimeOffset.Parse("2025-01-01T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture)
        );
        var context = CreateContext(nameof(MarkAsFailedAsync_Does_not_change_Completed_to_Failed));
        await using (context.ConfigureAwait(false))
        {
            var completed = CreateMessage(OutboxMessageStatus.Completed, fakeTime.GetUtcNow().AddMinutes(-10));
            _ = await context.OutboxMessages.AddAsync(completed, cancellationToken).ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            using var repository = new EntityFrameworkOutboxRepository<TestDbContext>(context, fakeTime);

            await repository.MarkAsFailedAsync(completed.Id, "boom", cancellationToken).ConfigureAwait(false);

            var reloaded = await context
                .OutboxMessages.SingleAsync(m => m.Id == completed.Id, cancellationToken)
                .ConfigureAwait(false);
            using (Assert.Multiple())
            {
                _ = await Assert.That(reloaded.Status).IsEqualTo(OutboxMessageStatus.Completed);
                _ = await Assert.That(reloaded.RetryCount).IsEqualTo(0);
                _ = await Assert.That(reloaded.Error).IsNull();
            }
        }
    }

    // INVARIANT (Q09): MarkAsCompletedAsync on a Processing message transitions it to
    // Completed and stamps ProcessedAt.
    [Test]
    public async Task MarkAsCompletedAsync_From_Processing_Transitions_to_Completed_With_ProcessedAt(
        CancellationToken cancellationToken
    )
    {
        var startTime = DateTimeOffset.Parse("2025-01-01T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var fakeTime = new FakeTimeProvider(startTime);
        var context = CreateContext(
            nameof(MarkAsCompletedAsync_From_Processing_Transitions_to_Completed_With_ProcessedAt)
        );
        await using (context.ConfigureAwait(false))
        {
            var processing = CreateMessage(OutboxMessageStatus.Processing, startTime.AddMinutes(-10));
            _ = await context.OutboxMessages.AddAsync(processing, cancellationToken).ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            using var repository = new EntityFrameworkOutboxRepository<TestDbContext>(context, fakeTime);

            await repository.MarkAsCompletedAsync(processing.Id, cancellationToken).ConfigureAwait(false);

            var reloaded = await context
                .OutboxMessages.SingleAsync(m => m.Id == processing.Id, cancellationToken)
                .ConfigureAwait(false);
            using (Assert.Multiple())
            {
                _ = await Assert.That(reloaded.Status).IsEqualTo(OutboxMessageStatus.Completed);
                _ = await Assert.That(reloaded.ProcessedAt).IsEqualTo(startTime);
                _ = await Assert.That(reloaded.UpdatedAt).IsEqualTo(startTime);
            }
        }
    }

    // INVARIANT (Q10): DeleteCompletedAsync must only remove Completed-status rows older
    // than the cutoff. Pending/Failed/DeadLetter rows of any age must survive.
    [Test]
    public async Task DeleteCompletedAsync_Removes_only_old_Completed_messages(CancellationToken cancellationToken)
    {
        var startTime = DateTimeOffset.Parse("2025-01-01T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var fakeTime = new FakeTimeProvider(startTime);
        var context = CreateContext(nameof(DeleteCompletedAsync_Removes_only_old_Completed_messages));
        await using (context.ConfigureAwait(false))
        {
            var oldCompleted = CreateMessage(
                OutboxMessageStatus.Completed,
                startTime.AddDays(-5),
                processedAt: startTime.AddDays(-5)
            );
            var recentCompleted = CreateMessage(
                OutboxMessageStatus.Completed,
                startTime.AddMinutes(-10),
                processedAt: startTime.AddMinutes(-10)
            );
            var oldFailed = CreateMessage(OutboxMessageStatus.Failed, startTime.AddDays(-5));
            var oldDeadLetter = CreateMessage(OutboxMessageStatus.DeadLetter, startTime.AddDays(-5));
            var oldPending = CreateMessage(OutboxMessageStatus.Pending, startTime.AddDays(-5));

            await context
                .OutboxMessages.AddRangeAsync(
                    new[] { oldCompleted, recentCompleted, oldFailed, oldDeadLetter, oldPending },
                    cancellationToken
                )
                .ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            using var repository = new EntityFrameworkOutboxRepository<TestDbContext>(context, fakeTime);

            // Cutoff = 1 day - only the 5-day-old Completed message qualifies.
            var deleted = await repository
                .DeleteCompletedAsync(TimeSpan.FromDays(1), cancellationToken)
                .ConfigureAwait(false);

            var remaining = await context.OutboxMessages.ToListAsync(cancellationToken).ConfigureAwait(false);
            using (Assert.Multiple())
            {
                _ = await Assert.That(deleted).IsEqualTo(1);
                _ = await Assert.That(remaining).HasCount(4);
                _ = await Assert.That(remaining.Exists(m => m.Id == oldCompleted.Id)).IsFalse();
                _ = await Assert.That(remaining.Exists(m => m.Id == recentCompleted.Id)).IsTrue();
                _ = await Assert.That(remaining.Exists(m => m.Id == oldFailed.Id)).IsTrue();
                _ = await Assert.That(remaining.Exists(m => m.Id == oldDeadLetter.Id)).IsTrue();
                _ = await Assert.That(remaining.Exists(m => m.Id == oldPending.Id)).IsTrue();
            }
        }
    }

    // INVARIANT (Q11): GetFailedForRetryAsync must respect both the retry-count ceiling
    // and the NextRetryAt schedule. Messages over the cap are NOT replayed.
    [Test]
    public async Task GetFailedForRetryAsync_Excludes_messages_at_or_above_max_retry_count(
        CancellationToken cancellationToken
    )
    {
        var startTime = DateTimeOffset.Parse("2025-01-01T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var fakeTime = new FakeTimeProvider(startTime);
        var context = CreateContext(nameof(GetFailedForRetryAsync_Excludes_messages_at_or_above_max_retry_count));
        await using (context.ConfigureAwait(false))
        {
            var underCap = CreateMessage(OutboxMessageStatus.Failed, startTime.AddMinutes(-10), retryCount: 2);
            var atCap = CreateMessage(OutboxMessageStatus.Failed, startTime.AddMinutes(-10), retryCount: 5);
            var overCap = CreateMessage(OutboxMessageStatus.Failed, startTime.AddMinutes(-10), retryCount: 7);
            await context
                .OutboxMessages.AddRangeAsync(new[] { underCap, atCap, overCap }, cancellationToken)
                .ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            using var repository = new EntityFrameworkOutboxRepository<TestDbContext>(context, fakeTime);

            var retried = await repository
                .GetFailedForRetryAsync(maxRetryCount: 5, batchSize: 10, cancellationToken)
                .ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(retried).HasCount(1);
                _ = await Assert.That(retried[0].Id).IsEqualTo(underCap.Id);
            }
        }
    }

    // INVARIANT (Q12): AddAsync rejects null and writes the message verbatim.
    [Test]
    public async Task AddAsync_Persists_message_and_AddAsync_with_null_throws(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(AddAsync_Persists_message_and_AddAsync_with_null_throws));
        await using (context.ConfigureAwait(false))
        {
            using var repository = new EntityFrameworkOutboxRepository<TestDbContext>(context, TimeProvider.System);

            // null path
            _ = await Assert
                .That(async () => await repository.AddAsync(null!, cancellationToken).ConfigureAwait(false))
                .Throws<ArgumentNullException>();

            // happy path
            var msg = CreateMessage(OutboxMessageStatus.Pending, DateTimeOffset.UtcNow);
            await repository.AddAsync(msg, cancellationToken).ConfigureAwait(false);

            _ = await Assert
                .That(await context.OutboxMessages.CountAsync(cancellationToken).ConfigureAwait(false))
                .IsEqualTo(1);
        }
    }

    // INVARIANT (Q12): Cancellation token is observed by Add/GetPending operations.
    [Test]
    public async Task GetPendingAsync_Honors_cancellation_token()
    {
        var context = CreateContext(nameof(GetPendingAsync_Honors_cancellation_token));
        await using (context.ConfigureAwait(false))
        {
            using var repository = new EntityFrameworkOutboxRepository<TestDbContext>(context, TimeProvider.System);

            using var cts = new CancellationTokenSource();
            await cts.CancelAsync().ConfigureAwait(false);

            _ = await Assert
                .That(async () => await repository.GetPendingAsync(10, cts.Token).ConfigureAwait(false))
                .Throws<OperationCanceledException>();
        }
    }
}
