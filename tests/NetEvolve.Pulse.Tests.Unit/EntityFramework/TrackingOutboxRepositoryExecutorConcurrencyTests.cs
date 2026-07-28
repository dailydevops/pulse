namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("EntityFramework")]
public sealed class TrackingOutboxRepositoryExecutorConcurrencyTests
{
    [Test]
    public async Task Model_StatusProperty_IsConcurrencyToken()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(Model_StatusProperty_IsConcurrencyToken))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var statusProperty = context
                .Model.FindEntityType(typeof(OutboxMessage))!
                .FindProperty(nameof(OutboxMessage.Status))!;

            _ = await Assert.That(statusProperty.IsConcurrencyToken).IsTrue();
        }
    }

    [Test]
    public async Task FetchAndMarkAsync_WhenCompetingPollerClaimsLoadedRows_DoesNotReturnLostRows(
        CancellationToken cancellationToken
    )
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var databaseName = nameof(FetchAndMarkAsync_WhenCompetingPollerClaimsLoadedRows_DoesNotReturnLostRows);

        var winnerOptions = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
        var loserOptions = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;

        var winnerContext = new TestDbContext(winnerOptions);
        await using (winnerContext.ConfigureAwait(false))
        {
            var loserContext = new TestDbContext(loserOptions);
            await using (loserContext.ConfigureAwait(false))
            {
                var messageId = Guid.NewGuid();
                _ = await winnerContext
                    .OutboxMessages.AddAsync(
                        new OutboxMessage
                        {
                            Id = messageId,
                            EventType = typeof(string),
                            Payload = "{}",
                            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                            Status = OutboxMessageStatus.Pending,
                        },
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                _ = await winnerContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                winnerContext.ChangeTracker.Clear();

                using var winnerExecutor = new InMemoryOutboxRepositoryExecutor<TestDbContext>(winnerContext, 1);
                using var loserExecutor = new InMemoryOutboxRepositoryExecutor<TestDbContext>(loserContext, 1);

                var winnerTimestamp = DateTimeOffset.UtcNow;
                var loserTimestamp = winnerTimestamp.AddMilliseconds(50);

                // The wrapped query lets a competing poller claim the rows after this
                // poller has loaded them but before it saves its own claim, reproducing
                // the read-modify-write race window.
                var interleavedQuery = new InterleavingAsyncQueryable<OutboxMessage>(
                    loserContext.OutboxMessages.Where(m => m.Status == OutboxMessageStatus.Pending),
                    async () =>
                        _ = await winnerExecutor
                            .FetchAndMarkAsync(
                                winnerContext.OutboxMessages.Where(m => m.Status == OutboxMessageStatus.Pending),
                                winnerTimestamp,
                                OutboxMessageStatus.Processing,
                                cancellationToken
                            )
                            .ConfigureAwait(false)
                );

                var loserResult = await loserExecutor
                    .FetchAndMarkAsync(
                        interleavedQuery,
                        loserTimestamp,
                        OutboxMessageStatus.Processing,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                var verificationContext = new TestDbContext(winnerOptions);
                await using (verificationContext.ConfigureAwait(false))
                {
                    var storedMessage = await verificationContext
                        .OutboxMessages.AsNoTracking()
                        .SingleAsync(m => m.Id == messageId, cancellationToken)
                        .ConfigureAwait(false);

                    using (Assert.Multiple())
                    {
                        _ = await Assert.That(loserResult).IsEmpty();
                        _ = await Assert.That(storedMessage.Status).IsEqualTo(OutboxMessageStatus.Processing);
                        _ = await Assert.That(storedMessage.UpdatedAt).IsEqualTo(winnerTimestamp);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Wraps an EF Core query so that a callback runs after the inner query has been fully
    /// enumerated but before the buffered results are handed to the caller.
    /// </summary>
    private sealed class InterleavingAsyncQueryable<T>(IQueryable<T> inner, Func<Task> afterEnumeration)
        : IQueryable<T>,
            IAsyncEnumerable<T>
    {
        public Type ElementType => inner.ElementType;

        public Expression Expression => inner.Expression;

        public IQueryProvider Provider => inner.Provider;

        public IEnumerator<T> GetEnumerator() => inner.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            var buffer = new List<T>();
            await foreach (
                var item in ((IAsyncEnumerable<T>)inner).WithCancellation(cancellationToken).ConfigureAwait(false)
            )
            {
                buffer.Add(item);
            }

            await afterEnumeration().ConfigureAwait(false);

            foreach (var item in buffer)
            {
                yield return item;
            }
        }
    }
}
