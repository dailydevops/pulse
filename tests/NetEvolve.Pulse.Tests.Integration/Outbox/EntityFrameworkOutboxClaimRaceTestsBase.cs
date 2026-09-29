namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.Outbox;

/// <summary>
/// Verifies that two Entity Framework outbox pollers never claim the same message when the second
/// poller's claim overlaps the first one and has to wait for its row locks.
/// </summary>
/// <remarks>
/// The first poller claims inside an open transaction, so its row locks stay held. The second poller
/// then reads the same candidates (the first claim is not yet committed) and its claim statement
/// blocks on those locks. Committing the first transaction releases the second claim, which must
/// re-check the claim predicate against the committed row versions and skip every row the first
/// poller took.
/// </remarks>
[TestGroup("Outbox")]
[Timeout(300_000)]
public abstract class EntityFrameworkOutboxClaimRaceTestsBase(
    IServiceFixture databaseServiceFixture,
    IServiceInitializer databaseInitializer
) : PulseTestsBase(databaseServiceFixture, databaseInitializer)
{
    private const int MessageCount = 5;

    private const int MaxRetryCount = 3;

    /// <summary>
    /// How long the second poller is given to reach the first poller's row locks before the first
    /// transaction commits.
    /// </summary>
    private static readonly TimeSpan OverlapWindow = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Hook for provider-specific database settings applied before the race runs, such as
    /// enabling <c>READ_COMMITTED_SNAPSHOT</c> on SQL Server.
    /// </summary>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    protected virtual Task PrepareDatabaseAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // Test names double as table names and must stay within MySQL's 64-character identifier limit.
    [Test]
    public async Task GetPendingAsync_WhenClaimsOverlap_ReturnsDisjointBatches(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    await PrepareDatabaseAsync(token).ConfigureAwait(false);
                    await AddMessagesAsync(services, OutboxMessageStatus.Pending, TimeSpan.Zero, token)
                        .ConfigureAwait(false);

                    var (first, second, overlapped) = await ClaimConcurrentlyAsync(
                            services,
                            outbox => outbox.GetPendingAsync(MessageCount * 2, token),
                            null,
                            token
                        )
                        .ConfigureAwait(false);

                    _ = await Assert.That(overlapped).IsTrue();
                    _ = await Assert.That(first.Count).IsEqualTo(MessageCount);
                    _ = await Assert.That(second.Select(m => m.Id).Intersect(first.Select(m => m.Id))).IsEmpty();
                },
                cancellationToken,
                configureServices: DisableProcessing
            )
            .ConfigureAwait(false);

    [Test]
    public async Task GetPendingAsync_WhenReclaimsOverlap_ReturnsDisjointBatches(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    await PrepareDatabaseAsync(token).ConfigureAwait(false);

                    // Processing rows whose default five-minute lease expired long ago, as left by a crashed worker.
                    await AddMessagesAsync(services, OutboxMessageStatus.Processing, TimeSpan.FromMinutes(10), token)
                        .ConfigureAwait(false);

                    var (first, second, overlapped) = await ClaimConcurrentlyAsync(
                            services,
                            outbox => outbox.GetPendingAsync(MessageCount * 2, token),
                            null,
                            token
                        )
                        .ConfigureAwait(false);

                    _ = await Assert.That(overlapped).IsTrue();
                    _ = await Assert.That(first.Count).IsEqualTo(MessageCount);
                    _ = await Assert.That(second.Select(m => m.Id).Intersect(first.Select(m => m.Id))).IsEmpty();
                },
                cancellationToken,
                configureServices: DisableProcessing
            )
            .ConfigureAwait(false);

    [Test]
    public async Task GetFailedForRetryAsync_WhenClaimsOverlap_ReturnsDisjointBatches(
        CancellationToken cancellationToken
    ) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    await PrepareDatabaseAsync(token).ConfigureAwait(false);
                    await AddMessagesAsync(services, OutboxMessageStatus.Failed, TimeSpan.Zero, token)
                        .ConfigureAwait(false);

                    var (first, second, overlapped) = await ClaimConcurrentlyAsync(
                            services,
                            outbox => outbox.GetFailedForRetryAsync(MaxRetryCount, MessageCount * 2, token),
                            null,
                            token
                        )
                        .ConfigureAwait(false);

                    _ = await Assert.That(overlapped).IsTrue();
                    _ = await Assert.That(first.Count).IsEqualTo(MessageCount);
                    _ = await Assert.That(second.Select(m => m.Id).Intersect(first.Select(m => m.Id))).IsEmpty();
                },
                cancellationToken,
                configureServices: DisableProcessing
            )
            .ConfigureAwait(false);

    [Test]
    public async Task GetFailedForRetryAsync_WhenRescheduled_IsNotReclaimed(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    await PrepareDatabaseAsync(token).ConfigureAwait(false);
                    await AddMessagesAsync(services, OutboxMessageStatus.Failed, TimeSpan.Zero, token)
                        .ConfigureAwait(false);

                    var nextRetryAt = services.GetRequiredService<TimeProvider>().GetUtcNow().AddHours(1);

                    var (first, second, overlapped) = await ClaimConcurrentlyAsync(
                            services,
                            outbox => outbox.GetFailedForRetryAsync(MaxRetryCount, MessageCount * 2, token),
                            async (outbox, claimed) =>
                            {
                                foreach (var message in claimed)
                                {
                                    await outbox
                                        .MarkAsFailedAsync(message.Id, "failed again", nextRetryAt, token)
                                        .ConfigureAwait(false);
                                }
                            },
                            token
                        )
                        .ConfigureAwait(false);

                    _ = await Assert.That(overlapped).IsTrue();
                    _ = await Assert.That(first.Count).IsEqualTo(MessageCount);
                    _ = await Assert.That(second).IsEmpty();
                },
                cancellationToken,
                configureServices: DisableProcessing
            )
            .ConfigureAwait(false);

    private static void DisableProcessing(IServiceCollection services) =>
        services.Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true);

    private static async Task AddMessagesAsync(
        IServiceProvider services,
        OutboxMessageStatus status,
        TimeSpan age,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = services.GetRequiredService<TimeProvider>().GetUtcNow() - age;
        var outbox = services.GetRequiredService<IOutboxRepository>();

        for (var i = 0; i < MessageCount; i++)
        {
            await outbox
                .AddAsync(
                    new OutboxMessage
                    {
                        Id = Guid.NewGuid(),
                        EventType = typeof(ClaimRaceEvent),
                        Payload = "{}",
                        CreatedAt = now.AddSeconds(-MessageCount + i),
                        UpdatedAt = now.AddSeconds(-MessageCount + i),
                        Status = status,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs <paramref name="claim"/> for a first poller inside an open transaction, starts the same
    /// claim for a second poller, and commits the first transaction only after the overlap window.
    /// </summary>
    /// <returns>
    /// Both claimed batches and whether the second claim was still running when the first poller
    /// committed, which proves the two claims overlapped.
    /// </returns>
    private static async Task<(
        IReadOnlyList<OutboxMessage> First,
        IReadOnlyList<OutboxMessage> Second,
        bool Overlapped
    )> ClaimConcurrentlyAsync(
        IServiceProvider services,
        Func<IOutboxRepository, Task<IReadOnlyList<OutboxMessage>>> claim,
        Func<IOutboxRepository, IReadOnlyList<OutboxMessage>, Task>? beforeCommit,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var firstScope = services.CreateAsyncScope();
        await using (firstScope.ConfigureAwait(false))
        {
            var secondScope = services.CreateAsyncScope();
            await using (secondScope.ConfigureAwait(false))
            {
                var firstContext =
                    firstScope.ServiceProvider.GetRequiredService<EntityFrameworkOutboxInitializer.TestDbContext>();
                var firstOutbox = firstScope.ServiceProvider.GetRequiredService<IOutboxRepository>();
                var secondOutbox = secondScope.ServiceProvider.GetRequiredService<IOutboxRepository>();

                // User-initiated transactions must run through the execution strategy when the provider
                // is configured with retries (SQL Server).
                return await firstContext
                    .Database.CreateExecutionStrategy()
                    .ExecuteAsync(async () =>
                    {
                        var transaction = await firstContext
                            .Database.BeginTransactionAsync(cancellationToken)
                            .ConfigureAwait(false);
                        await using (transaction.ConfigureAwait(false))
                        {
                            var first = await claim(firstOutbox).ConfigureAwait(false);

                            if (beforeCommit is not null)
                            {
                                await beforeCommit(firstOutbox, first).ConfigureAwait(false);
                            }

                            var secondTask = Task.Run(() => claim(secondOutbox), cancellationToken);
                            var completed = await Task.WhenAny(secondTask, Task.Delay(OverlapWindow, cancellationToken))
                                .ConfigureAwait(false);
                            var overlapped = completed != secondTask;

                            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                            var second = await secondTask.ConfigureAwait(false);

                            return (first, second, overlapped);
                        }
                    })
                    .ConfigureAwait(false);
            }
        }
    }

    private sealed record ClaimRaceEvent;
}
