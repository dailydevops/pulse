namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

/// <summary>
/// Tests for the single-message and bulk status-transition overloads of
/// <see cref="EntityFrameworkOutboxRepository{TContext}"/>
/// (<c>MarkAsCompletedAsync</c>, <c>MarkAsFailedAsync</c>, <c>MarkAsDeadLetterAsync</c>)
/// using the EF Core InMemory provider as the backing store.
/// </summary>
[TestGroup("EntityFramework")]
public sealed class EntityFrameworkOutboxRepositoryStatusTransitionTests
{
    private static readonly DateTimeOffset Start = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _timeProvider = new(Start);

    private static TestDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase($"transitions_{Guid.NewGuid():N}").Options
        );

    private EntityFrameworkOutboxRepository<TestDbContext> CreateRepository(TestDbContext context) =>
        new(context, Options.Create(new OutboxOptions()), _timeProvider);

    private static async Task<OutboxMessage> AddAsync(
        TestDbContext context,
        OutboxMessageStatus status,
        CancellationToken cancellationToken
    )
    {
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = typeof(TestEvent),
            Payload = "{}",
            CreatedAt = Start.AddMinutes(-10),
            UpdatedAt = Start.AddMinutes(-10),
            Status = status,
        };
        _ = await context.OutboxMessages.AddAsync(message, cancellationToken).ConfigureAwait(false);
        _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return message;
    }

    private static async Task<OutboxMessage> ReloadAsync(
        TestDbContext context,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        // Bypass the change tracker so assertions observe the stored state.
        var entry = context.ChangeTracker.Entries<OutboxMessage>().Single(e => e.Entity.Id == id);
        await entry.ReloadAsync(cancellationToken).ConfigureAwait(false);
        return entry.Entity;
    }

    private sealed record TestEvent;

    [Test]
    public async Task MarkAsCompletedAsync_Bulk_CompletesOnlyListedProcessingMessages(
        CancellationToken cancellationToken
    )
    {
        var context = CreateContext();
        await using (context.ConfigureAwait(false))
        {
            var a = await AddAsync(context, OutboxMessageStatus.Processing, cancellationToken).ConfigureAwait(false);
            var b = await AddAsync(context, OutboxMessageStatus.Processing, cancellationToken).ConfigureAwait(false);
            var notListed = await AddAsync(context, OutboxMessageStatus.Processing, cancellationToken)
                .ConfigureAwait(false);
            var wrongStatus = await AddAsync(context, OutboxMessageStatus.Pending, cancellationToken)
                .ConfigureAwait(false);
            using var repository = CreateRepository(context);

            await repository
                .MarkAsCompletedAsync(new List<Guid> { a.Id, b.Id, wrongStatus.Id }, cancellationToken)
                .ConfigureAwait(false);

            using (Assert.Multiple())
            {
                foreach (var completed in new[] { a, b })
                {
                    var reloaded = await ReloadAsync(context, completed.Id, cancellationToken).ConfigureAwait(false);
                    _ = await Assert.That(reloaded.Status).IsEqualTo(OutboxMessageStatus.Completed);
                    _ = await Assert.That(reloaded.ProcessedAt).IsEqualTo(Start);
                    _ = await Assert.That(reloaded.UpdatedAt).IsEqualTo(Start);
                    _ = await Assert.That(reloaded.RetryCount).IsEqualTo(0);
                }

                _ = await Assert
                    .That((await ReloadAsync(context, notListed.Id, cancellationToken).ConfigureAwait(false)).Status)
                    .IsEqualTo(OutboxMessageStatus.Processing);
                _ = await Assert
                    .That((await ReloadAsync(context, wrongStatus.Id, cancellationToken).ConfigureAwait(false)).Status)
                    .IsEqualTo(OutboxMessageStatus.Pending);
            }
        }
    }

    [Test]
    public async Task BulkOverloads_WithNullOrEmptyIds_AreNoOps(CancellationToken cancellationToken)
    {
        var context = CreateContext();
        await using (context.ConfigureAwait(false))
        {
            var message = await AddAsync(context, OutboxMessageStatus.Processing, cancellationToken)
                .ConfigureAwait(false);
            using var repository = CreateRepository(context);

            await repository
                .MarkAsCompletedAsync((IReadOnlyCollection<Guid>)null!, cancellationToken)
                .ConfigureAwait(false);
            await repository.MarkAsCompletedAsync(new List<Guid>(), cancellationToken).ConfigureAwait(false);
            await repository
                .MarkAsFailedAsync((IReadOnlyCollection<Guid>)null!, "boom", cancellationToken)
                .ConfigureAwait(false);
            await repository.MarkAsFailedAsync(new List<Guid>(), "boom", cancellationToken).ConfigureAwait(false);
            await repository
                .MarkAsDeadLetterAsync((IReadOnlyCollection<Guid>)null!, "boom", cancellationToken)
                .ConfigureAwait(false);
            await repository.MarkAsDeadLetterAsync(new List<Guid>(), "boom", cancellationToken).ConfigureAwait(false);

            var reloaded = await ReloadAsync(context, message.Id, cancellationToken).ConfigureAwait(false);
            using (Assert.Multiple())
            {
                _ = await Assert.That(reloaded.Status).IsEqualTo(OutboxMessageStatus.Processing);
                _ = await Assert.That(reloaded.Error).IsNull();
            }
        }
    }

    [Test]
    public async Task MarkAsFailedAsync_Single_FailsProcessingMessageAndIncrementsRetryCount(
        CancellationToken cancellationToken
    )
    {
        var context = CreateContext();
        await using (context.ConfigureAwait(false))
        {
            var message = await AddAsync(context, OutboxMessageStatus.Processing, cancellationToken)
                .ConfigureAwait(false);
            using var repository = CreateRepository(context);

            await repository.MarkAsFailedAsync(message.Id, "boom", cancellationToken).ConfigureAwait(false);

            var reloaded = await ReloadAsync(context, message.Id, cancellationToken).ConfigureAwait(false);
            using (Assert.Multiple())
            {
                _ = await Assert.That(reloaded.Status).IsEqualTo(OutboxMessageStatus.Failed);
                _ = await Assert.That(reloaded.RetryCount).IsEqualTo(1);
                _ = await Assert.That(reloaded.Error).IsEqualTo("boom");
                _ = await Assert.That(reloaded.NextRetryAt).IsNull();
                _ = await Assert.That(reloaded.UpdatedAt).IsEqualTo(Start);
            }
        }
    }

    [Test]
    public async Task MarkAsFailedAsync_WithNextRetryAt_SchedulesRetry(CancellationToken cancellationToken)
    {
        var context = CreateContext();
        await using (context.ConfigureAwait(false))
        {
            var message = await AddAsync(context, OutboxMessageStatus.Processing, cancellationToken)
                .ConfigureAwait(false);
            var nextRetryAt = Start.AddMinutes(7);
            using var repository = CreateRepository(context);

            await repository
                .MarkAsFailedAsync(message.Id, "boom", nextRetryAt, cancellationToken)
                .ConfigureAwait(false);

            var reloaded = await ReloadAsync(context, message.Id, cancellationToken).ConfigureAwait(false);
            using (Assert.Multiple())
            {
                _ = await Assert.That(reloaded.Status).IsEqualTo(OutboxMessageStatus.Failed);
                _ = await Assert.That(reloaded.RetryCount).IsEqualTo(1);
                _ = await Assert.That(reloaded.NextRetryAt).IsEqualTo(nextRetryAt);
            }
        }
    }

    [Test]
    public async Task MarkAsFailedAsync_WithNextRetryAt_IgnoresNonProcessingMessage(CancellationToken cancellationToken)
    {
        var context = CreateContext();
        await using (context.ConfigureAwait(false))
        {
            var message = await AddAsync(context, OutboxMessageStatus.Completed, cancellationToken)
                .ConfigureAwait(false);
            using var repository = CreateRepository(context);

            await repository
                .MarkAsFailedAsync(message.Id, "boom", Start.AddMinutes(7), cancellationToken)
                .ConfigureAwait(false);

            var reloaded = await ReloadAsync(context, message.Id, cancellationToken).ConfigureAwait(false);
            using (Assert.Multiple())
            {
                _ = await Assert.That(reloaded.Status).IsEqualTo(OutboxMessageStatus.Completed);
                _ = await Assert.That(reloaded.RetryCount).IsEqualTo(0);
                _ = await Assert.That(reloaded.NextRetryAt).IsNull();
            }
        }
    }

    [Test]
    public async Task MarkAsFailedAsync_Bulk_FailsListedProcessingMessages(CancellationToken cancellationToken)
    {
        var context = CreateContext();
        await using (context.ConfigureAwait(false))
        {
            var a = await AddAsync(context, OutboxMessageStatus.Processing, cancellationToken).ConfigureAwait(false);
            var b = await AddAsync(context, OutboxMessageStatus.Processing, cancellationToken).ConfigureAwait(false);
            var notListed = await AddAsync(context, OutboxMessageStatus.Processing, cancellationToken)
                .ConfigureAwait(false);
            using var repository = CreateRepository(context);

            await repository
                .MarkAsFailedAsync(new List<Guid> { a.Id, b.Id }, "boom", cancellationToken)
                .ConfigureAwait(false);

            using (Assert.Multiple())
            {
                foreach (var failed in new[] { a, b })
                {
                    var reloaded = await ReloadAsync(context, failed.Id, cancellationToken).ConfigureAwait(false);
                    _ = await Assert.That(reloaded.Status).IsEqualTo(OutboxMessageStatus.Failed);
                    _ = await Assert.That(reloaded.RetryCount).IsEqualTo(1);
                    _ = await Assert.That(reloaded.Error).IsEqualTo("boom");
                    _ = await Assert.That(reloaded.ProcessedAt).IsNull();
                }

                _ = await Assert
                    .That((await ReloadAsync(context, notListed.Id, cancellationToken).ConfigureAwait(false)).Status)
                    .IsEqualTo(OutboxMessageStatus.Processing);
            }
        }
    }

    [Test]
    public async Task MarkAsDeadLetterAsync_Single_MovesProcessingMessageWithoutRetryIncrement(
        CancellationToken cancellationToken
    )
    {
        var context = CreateContext();
        await using (context.ConfigureAwait(false))
        {
            var processing = await AddAsync(context, OutboxMessageStatus.Processing, cancellationToken)
                .ConfigureAwait(false);
            var bystander = await AddAsync(context, OutboxMessageStatus.Processing, cancellationToken)
                .ConfigureAwait(false);
            var pending = await AddAsync(context, OutboxMessageStatus.Pending, cancellationToken).ConfigureAwait(false);
            using var repository = CreateRepository(context);

            await repository.MarkAsDeadLetterAsync(processing.Id, "fatal", cancellationToken).ConfigureAwait(false);
            await repository.MarkAsDeadLetterAsync(pending.Id, "fatal", cancellationToken).ConfigureAwait(false);

            var moved = await ReloadAsync(context, processing.Id, cancellationToken).ConfigureAwait(false);
            var untouchedProcessing = await ReloadAsync(context, bystander.Id, cancellationToken).ConfigureAwait(false);
            var untouched = await ReloadAsync(context, pending.Id, cancellationToken).ConfigureAwait(false);
            using (Assert.Multiple())
            {
                _ = await Assert.That(moved.Status).IsEqualTo(OutboxMessageStatus.DeadLetter);
                _ = await Assert.That(moved.RetryCount).IsEqualTo(0);
                _ = await Assert.That(moved.Error).IsEqualTo("fatal");
                _ = await Assert.That(moved.UpdatedAt).IsEqualTo(Start);
                _ = await Assert.That(untouchedProcessing.Status).IsEqualTo(OutboxMessageStatus.Processing);
                _ = await Assert.That(untouched.Status).IsEqualTo(OutboxMessageStatus.Pending);
                _ = await Assert.That(untouched.Error).IsNull();
            }
        }
    }

    [Test]
    public async Task MarkAsDeadLetterAsync_Bulk_MovesListedProcessingMessages(CancellationToken cancellationToken)
    {
        var context = CreateContext();
        await using (context.ConfigureAwait(false))
        {
            var a = await AddAsync(context, OutboxMessageStatus.Processing, cancellationToken).ConfigureAwait(false);
            var b = await AddAsync(context, OutboxMessageStatus.Processing, cancellationToken).ConfigureAwait(false);
            var notListed = await AddAsync(context, OutboxMessageStatus.Processing, cancellationToken)
                .ConfigureAwait(false);
            using var repository = CreateRepository(context);

            await repository
                .MarkAsDeadLetterAsync(new List<Guid> { a.Id, b.Id }, "fatal", cancellationToken)
                .ConfigureAwait(false);

            using (Assert.Multiple())
            {
                foreach (var moved in new[] { a, b })
                {
                    var reloaded = await ReloadAsync(context, moved.Id, cancellationToken).ConfigureAwait(false);
                    _ = await Assert.That(reloaded.Status).IsEqualTo(OutboxMessageStatus.DeadLetter);
                    _ = await Assert.That(reloaded.RetryCount).IsEqualTo(0);
                    _ = await Assert.That(reloaded.Error).IsEqualTo("fatal");
                }

                _ = await Assert
                    .That((await ReloadAsync(context, notListed.Id, cancellationToken).ConfigureAwait(false)).Status)
                    .IsEqualTo(OutboxMessageStatus.Processing);
            }
        }
    }

    [Test]
    public async Task StatusTransitions_WithCancelledToken_ThrowOperationCanceledException()
    {
        var context = CreateContext();
        await using (context.ConfigureAwait(false))
        {
            using var repository = CreateRepository(context);
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync().ConfigureAwait(false);
            var ids = new List<Guid> { Guid.NewGuid() };

            using (Assert.Multiple())
            {
                _ = await Assert
                    .That(async () => await repository.MarkAsCompletedAsync(ids, cts.Token).ConfigureAwait(false))
                    .Throws<OperationCanceledException>();
                _ = await Assert
                    .That(async () =>
                        await repository.MarkAsFailedAsync(Guid.NewGuid(), "e", cts.Token).ConfigureAwait(false)
                    )
                    .Throws<OperationCanceledException>();
                _ = await Assert
                    .That(async () =>
                        await repository.MarkAsFailedAsync(Guid.NewGuid(), "e", null, cts.Token).ConfigureAwait(false)
                    )
                    .Throws<OperationCanceledException>();
                _ = await Assert
                    .That(async () => await repository.MarkAsFailedAsync(ids, "e", cts.Token).ConfigureAwait(false))
                    .Throws<OperationCanceledException>();
                _ = await Assert
                    .That(async () =>
                        await repository.MarkAsDeadLetterAsync(Guid.NewGuid(), "e", cts.Token).ConfigureAwait(false)
                    )
                    .Throws<OperationCanceledException>();
                _ = await Assert
                    .That(async () => await repository.MarkAsDeadLetterAsync(ids, "e", cts.Token).ConfigureAwait(false))
                    .Throws<OperationCanceledException>();
            }
        }
    }
}
