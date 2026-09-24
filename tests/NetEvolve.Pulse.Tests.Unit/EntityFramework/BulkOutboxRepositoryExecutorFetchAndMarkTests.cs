namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("EntityFramework")]
public sealed class BulkOutboxRepositoryExecutorFetchAndMarkTests
{
    [Test]
    public async Task FetchAndMarkAsync_WhenCompetingPollerClaimsSelectedRows_DoesNotReturnLostRows(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connectionString =
            "Data Source=FetchAndMarkClaimRace;Mode=Memory;Cache=Shared;Foreign Keys=False;Pooling=False";

        var keeperConnection = new SqliteConnection(connectionString);
        await using (keeperConnection.ConfigureAwait(false))
        {
            await keeperConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var winnerOptions = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connectionString).Options;
            var winnerContext = new TestDbContext(winnerOptions);
            await using (winnerContext.ConfigureAwait(false))
            {
                _ = await winnerContext.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

                var message = new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    EventType = typeof(string),
                    Payload = "{}",
                    CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                    UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                    Status = OutboxMessageStatus.Pending,
                };
                _ = await winnerContext.OutboxMessages.AddAsync(message, cancellationToken).ConfigureAwait(false);
                _ = await winnerContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                winnerContext.ChangeTracker.Clear();

                using var winnerExecutor = new BulkOutboxRepositoryExecutor<TestDbContext>(winnerContext, 1);

                var winnerTimestamp = DateTimeOffset.UtcNow;
                var loserTimestamp = winnerTimestamp.AddMilliseconds(50);

                var interceptor = new CompetingClaimInterceptor(async () =>
                    _ = await winnerExecutor
                        .FetchAndMarkAsync(
                            winnerContext
                                .OutboxMessages.Where(m => m.Status == OutboxMessageStatus.Pending)
                                .OrderBy(m => m.CreatedAt)
                                .Take(10),
                            winnerTimestamp,
                            OutboxMessageStatus.Processing,
                            cancellationToken
                        )
                        .ConfigureAwait(false)
                );

                var loserOptions = new DbContextOptionsBuilder<TestDbContext>()
                    .UseSqlite(connectionString)
                    .AddInterceptors(interceptor)
                    .Options;
                var loserContext = new TestDbContext(loserOptions);
                await using (loserContext.ConfigureAwait(false))
                {
                    using var loserExecutor = new BulkOutboxRepositoryExecutor<TestDbContext>(loserContext, 1);

                    var loserResult = await loserExecutor
                        .FetchAndMarkAsync(
                            loserContext
                                .OutboxMessages.Where(m => m.Status == OutboxMessageStatus.Pending)
                                .OrderBy(m => m.CreatedAt)
                                .Take(10),
                            loserTimestamp,
                            OutboxMessageStatus.Processing,
                            cancellationToken
                        )
                        .ConfigureAwait(false);

                    var storedMessage = await winnerContext
                        .OutboxMessages.AsNoTracking()
                        .SingleAsync(m => m.Id == message.Id, cancellationToken)
                        .ConfigureAwait(false);

                    using (Assert.Multiple())
                    {
                        _ = await Assert.That(interceptor.CompetingClaimExecuted).IsTrue();
                        _ = await Assert.That(loserResult).IsEmpty();
                        _ = await Assert.That(storedMessage.Status).IsEqualTo(OutboxMessageStatus.Processing);
                        _ = await Assert.That(storedMessage.UpdatedAt).IsEqualTo(winnerTimestamp);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Runs a competing claim right before the intercepted context executes its
    /// <c>ExecuteUpdateAsync</c> statement, reproducing the window between the candidate
    /// SELECT and the claiming UPDATE of a concurrent poller.
    /// </summary>
    private sealed class CompetingClaimInterceptor(Func<Task> competingClaim) : DbCommandInterceptor
    {
        public bool CompetingClaimExecuted { get; private set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!CompetingClaimExecuted)
            {
                CompetingClaimExecuted = true;
                await competingClaim().ConfigureAwait(false);
            }

            return result;
        }
    }
}
