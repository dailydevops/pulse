namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

/// <summary>
/// Verifies the shape of the claiming <c>UPDATE</c> issued by the bulk executor. The claim predicate
/// must sit on the updated table itself (no join back to a limited subquery), so that databases which
/// re-check the <c>WHERE</c> clause of a blocked update against the new row version (PostgreSQL
/// READ COMMITTED) skip rows a competing poller has already claimed.
/// </summary>
[TestGroup("EntityFramework")]
public sealed class EntityFrameworkOutboxRepositoryClaimStatementTests
{
    [Test]
    public async Task GetPendingAsync_ClaimUpdate_FiltersTargetRowsWithoutLimitedSubquery(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var claim = await CaptureClaimUpdateAsync(
                nameof(GetPendingAsync_ClaimUpdate_FiltersTargetRowsWithoutLimitedSubquery),
                OutboxMessageStatus.Pending,
                (repository, token) => repository.GetPendingAsync(10, token),
                cancellationToken
            )
            .ConfigureAwait(false);

        var where = WhereClause(claim);

        using (Assert.Multiple())
        {
            _ = await Assert.That(claim).DoesNotContain("FROM (", StringComparison.OrdinalIgnoreCase);
            _ = await Assert.That(claim).DoesNotContain("LIMIT", StringComparison.OrdinalIgnoreCase);
            _ = await Assert.That(claim).DoesNotContain("ORDER BY", StringComparison.OrdinalIgnoreCase);
            _ = await Assert.That(where).Contains("\"o\".\"Status\" = ", StringComparison.Ordinal);
            _ = await Assert.That(where).Contains("\"o\".\"NextRetryAt\"", StringComparison.Ordinal);
        }
    }

    [Test]
    public async Task GetFailedForRetryAsync_ClaimUpdate_FiltersTargetRowsWithoutLimitedSubquery(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var claim = await CaptureClaimUpdateAsync(
                nameof(GetFailedForRetryAsync_ClaimUpdate_FiltersTargetRowsWithoutLimitedSubquery),
                OutboxMessageStatus.Failed,
                (repository, token) => repository.GetFailedForRetryAsync(3, 10, token),
                cancellationToken
            )
            .ConfigureAwait(false);

        var where = WhereClause(claim);

        using (Assert.Multiple())
        {
            _ = await Assert.That(claim).DoesNotContain("FROM (", StringComparison.OrdinalIgnoreCase);
            _ = await Assert.That(claim).DoesNotContain("LIMIT", StringComparison.OrdinalIgnoreCase);
            _ = await Assert.That(claim).DoesNotContain("ORDER BY", StringComparison.OrdinalIgnoreCase);
            _ = await Assert.That(where).Contains("\"o\".\"Status\" = ", StringComparison.Ordinal);
            _ = await Assert.That(where).Contains("\"o\".\"RetryCount\" < ", StringComparison.Ordinal);
            _ = await Assert.That(where).Contains("\"o\".\"NextRetryAt\"", StringComparison.Ordinal);
        }
    }

    private static string WhereClause(string statement)
    {
        const string keyword = "WHERE";
        var index = statement.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        return index < 0 ? string.Empty : statement[(index + keyword.Length)..];
    }

    private static async Task<string> CaptureClaimUpdateAsync(
        string databaseName,
        OutboxMessageStatus status,
        Func<EntityFrameworkOutboxRepository<TestDbContext>, CancellationToken, Task> claim,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared;Pooling=False";

        var keeperConnection = new SqliteConnection(connectionString);
        await using (keeperConnection.ConfigureAwait(false))
        {
            await keeperConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var recorder = new UpdateCommandRecorder();
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlite(connectionString)
                .AddInterceptors(recorder)
                .Options;
            var context = new TestDbContext(options);
            await using (context.ConfigureAwait(false))
            {
                _ = await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

                var createdAt = DateTimeOffset.UtcNow.AddMinutes(-5);
                _ = await context
                    .OutboxMessages.AddAsync(
                        new OutboxMessage
                        {
                            Id = Guid.NewGuid(),
                            EventType = typeof(string),
                            Payload = "{}",
                            CreatedAt = createdAt,
                            UpdatedAt = createdAt,
                            Status = status,
                        },
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                context.ChangeTracker.Clear();
                recorder.Commands.Clear();

                using var repository = new EntityFrameworkOutboxRepository<TestDbContext>(
                    context,
                    Options.Create(new OutboxOptions()),
                    TimeProvider.System
                );
                await claim(repository, cancellationToken).ConfigureAwait(false);

                return recorder.Commands.Single(c => c.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    private sealed class UpdateCommandRecorder : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        [SuppressMessage(
            "Usage",
            "NE0009:Method or local function has a CancellationToken parameter but does not check for cancellation at the start of its body",
            Justification = "This recording test double must record the call before honoring cancellation, so tests can assert that the call happened even when the token is already cancelled."
        )]
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            Commands.Add(command.CommandText.Trim());
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(result);
        }
    }
}
