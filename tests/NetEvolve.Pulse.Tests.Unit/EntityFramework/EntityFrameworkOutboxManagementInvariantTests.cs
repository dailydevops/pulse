namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

/// <summary>
/// Behavioral invariants for <see cref="EntityFrameworkOutboxManagement{TContext}"/>:
/// dead-letter inspection and replay must never alter rows in other states.
/// </summary>
[TestGroup("EntityFramework")]
public sealed class EntityFrameworkOutboxManagementInvariantTests
{
    private sealed record TestEvent;

    private static TestDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(databaseName).Options;
        return new TestDbContext(options);
    }

    private static OutboxMessage CreateMessage(
        OutboxMessageStatus status,
        DateTimeOffset createdAt,
        int retryCount = 0,
        string? error = null
    ) =>
        new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = typeof(TestEvent),
            Payload = "{}",
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            RetryCount = retryCount,
            Error = error,
            Status = status,
        };

    // INVARIANT (Q11): ReplayMessageAsync must only act on DeadLetter rows.
    // Replaying a Pending/Failed/Completed message would corrupt the queue.
    [Test]
    public async Task ReplayMessageAsync_Does_not_act_on_non_DeadLetter(CancellationToken cancellationToken)
    {
        var startTime = DateTimeOffset.Parse("2025-01-01T12:00:00Z", CultureInfo.InvariantCulture);
        var fakeTime = new FakeTimeProvider(startTime);
        var context = CreateContext(nameof(ReplayMessageAsync_Does_not_act_on_non_DeadLetter));
        await using (context.ConfigureAwait(false))
        {
            var failed = CreateMessage(
                OutboxMessageStatus.Failed,
                startTime.AddMinutes(-10),
                retryCount: 3,
                error: "oops"
            );
            _ = await context.OutboxMessages.AddAsync(failed, cancellationToken).ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var mgmt = new EntityFrameworkOutboxManagement<TestDbContext>(context, fakeTime);

            var result = await mgmt.ReplayMessageAsync(failed.Id, cancellationToken).ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(result).IsFalse();
                var reloaded = await context
                    .OutboxMessages.SingleAsync(m => m.Id == failed.Id, cancellationToken)
                    .ConfigureAwait(false);
                _ = await Assert.That(reloaded.Status).IsEqualTo(OutboxMessageStatus.Failed);
                _ = await Assert.That(reloaded.RetryCount).IsEqualTo(3);
                _ = await Assert.That(reloaded.Error).IsEqualTo("oops");
            }
        }
    }

    // INVARIANT (Q11): ReplayMessageAsync on a DLQ message transitions it to Pending and
    // clears retry count + error so the processor will pick it up again.
    [Test]
    public async Task ReplayMessageAsync_From_DeadLetter_Transitions_to_Pending_and_clears_error(
        CancellationToken cancellationToken
    )
    {
        var startTime = DateTimeOffset.Parse("2025-01-01T12:00:00Z", CultureInfo.InvariantCulture);
        var fakeTime = new FakeTimeProvider(startTime);
        var context = CreateContext(nameof(ReplayMessageAsync_From_DeadLetter_Transitions_to_Pending_and_clears_error));
        await using (context.ConfigureAwait(false))
        {
            var dlq = CreateMessage(
                OutboxMessageStatus.DeadLetter,
                startTime.AddMinutes(-10),
                retryCount: 5,
                error: "final"
            );
            _ = await context.OutboxMessages.AddAsync(dlq, cancellationToken).ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var mgmt = new EntityFrameworkOutboxManagement<TestDbContext>(context, fakeTime);

            var result = await mgmt.ReplayMessageAsync(dlq.Id, cancellationToken).ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(result).IsTrue();
                // Clear EF tracker so we re-read from store.
                foreach (var entry in context.ChangeTracker.Entries())
                {
                    entry.State = EntityState.Detached;
                }
                var reloaded = await context
                    .OutboxMessages.SingleAsync(m => m.Id == dlq.Id, cancellationToken)
                    .ConfigureAwait(false);
                _ = await Assert.That(reloaded.Status).IsEqualTo(OutboxMessageStatus.Pending);
                _ = await Assert.That(reloaded.RetryCount).IsEqualTo(0);
                _ = await Assert.That(reloaded.Error).IsNull();
                _ = await Assert.That(reloaded.UpdatedAt).IsEqualTo(startTime);
            }
        }
    }

    // INVARIANT (Q11): ReplayAllDeadLetterAsync resets every DLQ message and does not touch
    // anything else. The return count equals the number of resets.
    [Test]
    public async Task ReplayAllDeadLetterAsync_Resets_only_DLQ_messages(CancellationToken cancellationToken)
    {
        var startTime = DateTimeOffset.Parse("2025-01-01T12:00:00Z", CultureInfo.InvariantCulture);
        var fakeTime = new FakeTimeProvider(startTime);
        var context = CreateContext(nameof(ReplayAllDeadLetterAsync_Resets_only_DLQ_messages));
        await using (context.ConfigureAwait(false))
        {
            var dlq1 = CreateMessage(OutboxMessageStatus.DeadLetter, startTime.AddMinutes(-10), retryCount: 7);
            var dlq2 = CreateMessage(OutboxMessageStatus.DeadLetter, startTime.AddMinutes(-10), retryCount: 5);
            var pending = CreateMessage(OutboxMessageStatus.Pending, startTime.AddMinutes(-10));
            var completed = CreateMessage(OutboxMessageStatus.Completed, startTime.AddMinutes(-10));

            await context
                .OutboxMessages.AddRangeAsync(new[] { dlq1, dlq2, pending, completed }, cancellationToken)
                .ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var mgmt = new EntityFrameworkOutboxManagement<TestDbContext>(context, fakeTime);

            var count = await mgmt.ReplayAllDeadLetterAsync(cancellationToken).ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(count).IsEqualTo(2);

                foreach (var entry in context.ChangeTracker.Entries())
                {
                    entry.State = EntityState.Detached;
                }

                var dlq1Reloaded = await context
                    .OutboxMessages.SingleAsync(m => m.Id == dlq1.Id, cancellationToken)
                    .ConfigureAwait(false);
                var dlq2Reloaded = await context
                    .OutboxMessages.SingleAsync(m => m.Id == dlq2.Id, cancellationToken)
                    .ConfigureAwait(false);
                var pendingReloaded = await context
                    .OutboxMessages.SingleAsync(m => m.Id == pending.Id, cancellationToken)
                    .ConfigureAwait(false);
                var completedReloaded = await context
                    .OutboxMessages.SingleAsync(m => m.Id == completed.Id, cancellationToken)
                    .ConfigureAwait(false);

                _ = await Assert.That(dlq1Reloaded.Status).IsEqualTo(OutboxMessageStatus.Pending);
                _ = await Assert.That(dlq1Reloaded.RetryCount).IsEqualTo(0);
                _ = await Assert.That(dlq2Reloaded.Status).IsEqualTo(OutboxMessageStatus.Pending);
                _ = await Assert.That(dlq2Reloaded.RetryCount).IsEqualTo(0);
                _ = await Assert.That(pendingReloaded.Status).IsEqualTo(OutboxMessageStatus.Pending);
                _ = await Assert.That(completedReloaded.Status).IsEqualTo(OutboxMessageStatus.Completed);
            }
        }
    }

    // INVARIANT (Q11): GetDeadLetterMessageAsync returns null for a Pending row even if its id matches.
    // The filter is "Id == id AND Status == DeadLetter" — non-DLQ rows must not leak through.
    [Test]
    public async Task GetDeadLetterMessageAsync_Returns_null_for_non_DLQ_message(CancellationToken cancellationToken)
    {
        var startTime = DateTimeOffset.Parse("2025-01-01T12:00:00Z", CultureInfo.InvariantCulture);
        var fakeTime = new FakeTimeProvider(startTime);
        var context = CreateContext(nameof(GetDeadLetterMessageAsync_Returns_null_for_non_DLQ_message));
        await using (context.ConfigureAwait(false))
        {
            var pending = CreateMessage(OutboxMessageStatus.Pending, startTime.AddMinutes(-10));
            _ = await context.OutboxMessages.AddAsync(pending, cancellationToken).ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var mgmt = new EntityFrameworkOutboxManagement<TestDbContext>(context, fakeTime);

            var result = await mgmt.GetDeadLetterMessageAsync(pending.Id, cancellationToken).ConfigureAwait(false);

            _ = await Assert.That(result).IsNull();
        }
    }

    // INVARIANT (Q11): Statistics aggregate correctly across all five statuses.
    [Test]
    public async Task GetStatisticsAsync_Counts_each_status_correctly(CancellationToken cancellationToken)
    {
        var startTime = DateTimeOffset.Parse("2025-01-01T12:00:00Z", CultureInfo.InvariantCulture);
        var fakeTime = new FakeTimeProvider(startTime);
        var context = CreateContext(nameof(GetStatisticsAsync_Counts_each_status_correctly));
        await using (context.ConfigureAwait(false))
        {
            var msgs = new[]
            {
                CreateMessage(OutboxMessageStatus.Pending, startTime),
                CreateMessage(OutboxMessageStatus.Pending, startTime),
                CreateMessage(OutboxMessageStatus.Processing, startTime),
                CreateMessage(OutboxMessageStatus.Completed, startTime),
                CreateMessage(OutboxMessageStatus.Completed, startTime),
                CreateMessage(OutboxMessageStatus.Completed, startTime),
                CreateMessage(OutboxMessageStatus.Failed, startTime),
                CreateMessage(OutboxMessageStatus.DeadLetter, startTime),
            };

            await context.OutboxMessages.AddRangeAsync(msgs, cancellationToken).ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var mgmt = new EntityFrameworkOutboxManagement<TestDbContext>(context, fakeTime);

            var stats = await mgmt.GetStatisticsAsync(cancellationToken).ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(stats.Pending).IsEqualTo(2L);
                _ = await Assert.That(stats.Processing).IsEqualTo(1L);
                _ = await Assert.That(stats.Completed).IsEqualTo(3L);
                _ = await Assert.That(stats.Failed).IsEqualTo(1L);
                _ = await Assert.That(stats.DeadLetter).IsEqualTo(1L);
            }
        }
    }

    // INVARIANT (Q12): Page parameter validation - negative page rejected, zero pageSize rejected.
    [Test]
    public async Task GetDeadLetterMessagesAsync_With_negative_page_throws(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(GetDeadLetterMessagesAsync_With_negative_page_throws));
        await using (context.ConfigureAwait(false))
        {
            var mgmt = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

            _ = await Assert
                .That(async () =>
                    await mgmt.GetDeadLetterMessagesAsync(50, -1, cancellationToken).ConfigureAwait(false)
                )
                .Throws<ArgumentOutOfRangeException>();
        }
    }

    [Test]
    public async Task GetDeadLetterMessagesAsync_With_zero_pageSize_throws(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(GetDeadLetterMessagesAsync_With_zero_pageSize_throws));
        await using (context.ConfigureAwait(false))
        {
            var mgmt = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

            _ = await Assert
                .That(async () => await mgmt.GetDeadLetterMessagesAsync(0, 0, cancellationToken).ConfigureAwait(false))
                .Throws<ArgumentOutOfRangeException>();
        }
    }

    // INVARIANT (Q12): Overflow guard — `page * pageSize` must not silently overflow.
    [Test]
    public async Task GetDeadLetterMessagesAsync_With_overflowing_page_throws(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(GetDeadLetterMessagesAsync_With_overflowing_page_throws));
        await using (context.ConfigureAwait(false))
        {
            var mgmt = new EntityFrameworkOutboxManagement<TestDbContext>(context, TimeProvider.System);

            // page * pageSize overflows int.
            _ = await Assert
                .That(async () =>
                    await mgmt.GetDeadLetterMessagesAsync(int.MaxValue, int.MaxValue, cancellationToken)
                        .ConfigureAwait(false)
                )
                .Throws<ArgumentOutOfRangeException>();
        }
    }
}
