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
public sealed class TrackingOutboxRepositoryExecutorDeleteTests
{
    [Test]
    public async Task DeleteByQueryAsync_DoesNotMaterializePayloadColumn(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = new SqliteConnection("Data Source=:memory:");
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var interceptor = new RecordingCommandInterceptor();
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(interceptor)
                .Options;
            var context = new TestDbContext(options);
            await using (context.ConfigureAwait(false))
            {
                _ = await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

                var cutoff = DateTimeOffset.UtcNow;
                for (var i = 0; i < 3; i++)
                {
                    _ = await context
                        .OutboxMessages.AddAsync(
                            CreateMessage(OutboxMessageStatus.Completed, cutoff.AddMinutes(-10)),
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                }
                _ = await context
                    .OutboxMessages.AddAsync(CreateMessage(OutboxMessageStatus.Pending, null), cancellationToken)
                    .ConfigureAwait(false);
                _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                context.ChangeTracker.Clear();

                using var executor = new InMemoryOutboxRepositoryExecutor<TestDbContext>(context, 1);

                interceptor.StartRecording();

                var deleted = await executor
                    .DeleteByQueryAsync(
                        context.OutboxMessages.Where(m =>
                            m.Status == OutboxMessageStatus.Completed && m.ProcessedAt < cutoff
                        ),
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                var remaining = await context
                    .OutboxMessages.AsNoTracking()
                    .CountAsync(cancellationToken)
                    .ConfigureAwait(false);

                var payloadQueries = interceptor
                    .RecordedCommands.Where(c =>
                        c.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                        && c.Contains("Payload", StringComparison.OrdinalIgnoreCase)
                    )
                    .ToArray();

                using (Assert.Multiple())
                {
                    _ = await Assert.That(deleted).IsEqualTo(3);
                    _ = await Assert.That(remaining).IsEqualTo(1);
                    _ = await Assert.That(payloadQueries).IsEmpty();
                }
            }
        }
    }

    [Test]
    public async Task DeleteByQueryAsync_WithAlreadyTrackedEntity_DeletesAllMatches(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(nameof(DeleteByQueryAsync_WithAlreadyTrackedEntity_DeletesAllMatches))
            .Options;
        var context = new TestDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var cutoff = DateTimeOffset.UtcNow;
            var trackedMessage = CreateMessage(OutboxMessageStatus.Completed, cutoff.AddMinutes(-10));
            _ = await context.OutboxMessages.AddAsync(trackedMessage, cancellationToken).ConfigureAwait(false);
            _ = await context
                .OutboxMessages.AddAsync(
                    CreateMessage(OutboxMessageStatus.Completed, cutoff.AddMinutes(-10)),
                    cancellationToken
                )
                .ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // trackedMessage stays tracked; the executor must reuse the tracked instance
            // instead of attaching a conflicting stub with the same key.
            using var executor = new InMemoryOutboxRepositoryExecutor<TestDbContext>(context, 1);

            var deleted = await executor
                .DeleteByQueryAsync(
                    context.OutboxMessages.Where(m =>
                        m.Status == OutboxMessageStatus.Completed && m.ProcessedAt < cutoff
                    ),
                    cancellationToken
                )
                .ConfigureAwait(false);

            var remaining = await context
                .OutboxMessages.AsNoTracking()
                .CountAsync(cancellationToken)
                .ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(deleted).IsEqualTo(2);
                _ = await Assert.That(remaining).IsEqualTo(0);
            }
        }
    }

    private static OutboxMessage CreateMessage(OutboxMessageStatus status, DateTimeOffset? processedAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            EventType = typeof(string),
            Payload = new string('x', 4096),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            ProcessedAt = processedAt,
            Status = status,
        };

    private sealed class RecordingCommandInterceptor : DbCommandInterceptor
    {
        private readonly List<string> _commands = [];
        private bool _recording;

        public IReadOnlyList<string> RecordedCommands => _commands;

        public void StartRecording() => _recording = true;

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result
        )
        {
            Record(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            Record(command);
            return ValueTask.FromResult(result);
        }

        private void Record(DbCommand command)
        {
            if (_recording)
            {
                _commands.Add(command.CommandText);
            }
        }
    }
}
