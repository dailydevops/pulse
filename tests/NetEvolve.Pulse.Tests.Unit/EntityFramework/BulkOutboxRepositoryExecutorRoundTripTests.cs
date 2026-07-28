namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using System;
using System.Collections.Generic;
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
public sealed class BulkOutboxRepositoryExecutorRoundTripTests
{
    [Test]
    public async Task FetchAndMarkAsync_UncontendedBatch_UsesAtMostTwoRoundTrips(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var interceptor = new CountingCommandInterceptor();
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(interceptor)
                .Options;
            var context = new TestDbContext(options);
            await using (context.ConfigureAwait(false))
            {
                _ = await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

                var createdAt = DateTimeOffset.UtcNow.AddMinutes(-5);
                var messageIds = new List<Guid>();
                for (var i = 0; i < 3; i++)
                {
                    var message = new OutboxMessage
                    {
                        Id = Guid.NewGuid(),
                        EventType = typeof(string),
                        Payload = $"{{\"index\":{i}}}",
                        CreatedAt = createdAt.AddSeconds(i),
                        UpdatedAt = createdAt.AddSeconds(i),
                        Status = OutboxMessageStatus.Pending,
                    };
                    messageIds.Add(message.Id);
                    _ = await context.OutboxMessages.AddAsync(message, cancellationToken).ConfigureAwait(false);
                }
                _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                context.ChangeTracker.Clear();

                using var executor = new BulkOutboxRepositoryExecutor<TestDbContext>(context, 1);

                var updatedAt = DateTimeOffset.UtcNow;

                interceptor.StartCounting();

                var result = await executor
                    .FetchAndMarkAsync(
                        context
                            .OutboxMessages.Where(m => m.Status == OutboxMessageStatus.Pending)
                            .OrderBy(m => m.CreatedAt)
                            .Take(10),
                        updatedAt,
                        OutboxMessageStatus.Processing,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                var commandCount = interceptor.CommandCount;

                var storedStatuses = await context
                    .OutboxMessages.AsNoTracking()
                    .Select(m => m.Status)
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);

                using (Assert.Multiple())
                {
                    _ = await Assert.That(commandCount).IsLessThanOrEqualTo(2);
                    _ = await Assert.That(result).HasCount().EqualTo(3);
                    _ = await Assert.That(result.Select(m => m.Id)).Contains(messageIds[0]);
                    _ = await Assert
                        .That(result.All(m => m.Status == OutboxMessageStatus.Processing && m.UpdatedAt == updatedAt))
                        .IsTrue();
                    _ = await Assert.That(storedStatuses.All(s => s == OutboxMessageStatus.Processing)).IsTrue();
                }
            }
        }
    }

    [Test]
    public async Task FetchAndMarkAsync_WithoutPendingMessages_ReturnsEmpty(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var options = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connection).Options;
            var context = new TestDbContext(options);
            await using (context.ConfigureAwait(false))
            {
                _ = await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

                using var executor = new BulkOutboxRepositoryExecutor<TestDbContext>(context, 1);

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
    }

    private sealed class CountingCommandInterceptor : DbCommandInterceptor
    {
        private bool _counting;
        private int _commandCount;

        public int CommandCount => _commandCount;

        public void StartCounting() => _counting = true;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Count();
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            Count();
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default
        )
        {
            Count();
            return ValueTask.FromResult(result);
        }

        private void Count()
        {
            if (_counting)
            {
                _ = Interlocked.Increment(ref _commandCount);
            }
        }
    }
}
