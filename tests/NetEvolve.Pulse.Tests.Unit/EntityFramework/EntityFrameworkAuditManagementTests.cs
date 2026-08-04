namespace NetEvolve.Pulse.Tests.Unit.EntityFramework;

using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Audit;
using NetEvolve.Pulse.Extensibility.Audit;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("EntityFramework")]
public sealed class EntityFrameworkAuditManagementTests
{
    private static TestAuditStoreDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<TestAuditStoreDbContext>().UseInMemoryDatabase(databaseName).Options;
        return new TestAuditStoreDbContext(options);
    }

    private static AuditRecord CreateRecord(
        DateTimeOffset occurredAt,
        string commandType = "Some.Command",
        string? userId = null,
        AuditResult result = AuditResult.Success
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CommandType = commandType,
            UserId = userId,
            OccurredAt = occurredAt,
            Result = result,
        };

    [Test]
    public async Task Constructor_WithNullContext_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new EntityFrameworkAuditManagement<TestAuditStoreDbContext>(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithValidContext_CreatesInstance()
    {
        var context = CreateContext(nameof(Constructor_WithValidContext_CreatesInstance));
        await using (context.ConfigureAwait(false))
        {
            var management = new EntityFrameworkAuditManagement<TestAuditStoreDbContext>(context);

            _ = await Assert.That(management).IsNotNull();
        }
    }

    [Test]
    public async Task QueryAsync_WithNullFilter_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(QueryAsync_WithNullFilter_ThrowsArgumentNullException));
        await using (context.ConfigureAwait(false))
        {
            var management = new EntityFrameworkAuditManagement<TestAuditStoreDbContext>(context);

            _ = await Assert
                .That(async () => await management.QueryAsync(null!, cancellationToken).ConfigureAwait(false))
                .Throws<ArgumentNullException>();
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByCommandType(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(QueryAsync_FiltersByCommandType));
        await using (context.ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;
            var match = CreateRecord(now, commandType: "Match.Command");
            var noMatch = CreateRecord(now, commandType: "Other.Command");

            await context.AuditEntries.AddRangeAsync([match, noMatch], cancellationToken).ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var management = new EntityFrameworkAuditManagement<TestAuditStoreDbContext>(context);

            var result = await management
                .QueryAsync(new AuditFilter { CommandType = "Match.Command" }, cancellationToken)
                .ConfigureAwait(false);

            _ = await Assert.That(result).HasSingleItem();
            _ = await Assert.That(result[0].Id).IsEqualTo(match.Id);
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByUserId(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(QueryAsync_FiltersByUserId));
        await using (context.ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;
            var match = CreateRecord(now, userId: "user-1");
            var noMatch = CreateRecord(now, userId: "user-2");

            await context.AuditEntries.AddRangeAsync([match, noMatch], cancellationToken).ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var management = new EntityFrameworkAuditManagement<TestAuditStoreDbContext>(context);

            var result = await management
                .QueryAsync(new AuditFilter { UserId = "user-1" }, cancellationToken)
                .ConfigureAwait(false);

            _ = await Assert.That(result).HasSingleItem();
            _ = await Assert.That(result[0].Id).IsEqualTo(match.Id);
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByFrom(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(QueryAsync_FiltersByFrom));
        await using (context.ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;
            var older = CreateRecord(now.AddMinutes(-10));
            var newer = CreateRecord(now);

            await context.AuditEntries.AddRangeAsync([older, newer], cancellationToken).ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var management = new EntityFrameworkAuditManagement<TestAuditStoreDbContext>(context);

            var result = await management
                .QueryAsync(new AuditFilter { From = now.AddMinutes(-1) }, cancellationToken)
                .ConfigureAwait(false);

            _ = await Assert.That(result).HasSingleItem();
            _ = await Assert.That(result[0].Id).IsEqualTo(newer.Id);
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByTo(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(QueryAsync_FiltersByTo));
        await using (context.ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;
            var older = CreateRecord(now.AddMinutes(-10));
            var newer = CreateRecord(now);

            await context.AuditEntries.AddRangeAsync([older, newer], cancellationToken).ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var management = new EntityFrameworkAuditManagement<TestAuditStoreDbContext>(context);

            var result = await management
                .QueryAsync(new AuditFilter { To = now.AddMinutes(-1) }, cancellationToken)
                .ConfigureAwait(false);

            _ = await Assert.That(result).HasSingleItem();
            _ = await Assert.That(result[0].Id).IsEqualTo(older.Id);
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByResult(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(QueryAsync_FiltersByResult));
        await using (context.ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;
            var success = CreateRecord(now, result: AuditResult.Success);
            var failure = CreateRecord(now, result: AuditResult.Failure);

            await context.AuditEntries.AddRangeAsync([success, failure], cancellationToken).ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var management = new EntityFrameworkAuditManagement<TestAuditStoreDbContext>(context);

            var result = await management
                .QueryAsync(new AuditFilter { Result = AuditResult.Failure }, cancellationToken)
                .ConfigureAwait(false);

            _ = await Assert.That(result).HasSingleItem();
            _ = await Assert.That(result[0].Id).IsEqualTo(failure.Id);
        }
    }

    [Test]
    public async Task QueryAsync_CombinesMultipleFilterConditions(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(QueryAsync_CombinesMultipleFilterConditions));
        await using (context.ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;
            var match = CreateRecord(now, commandType: "Match.Command", userId: "user-1", result: AuditResult.Failure);
            var wrongCommandType = CreateRecord(
                now,
                commandType: "Other.Command",
                userId: "user-1",
                result: AuditResult.Failure
            );
            var wrongUserId = CreateRecord(
                now,
                commandType: "Match.Command",
                userId: "user-2",
                result: AuditResult.Failure
            );
            var wrongResult = CreateRecord(
                now,
                commandType: "Match.Command",
                userId: "user-1",
                result: AuditResult.Success
            );

            await context
                .AuditEntries.AddRangeAsync([match, wrongCommandType, wrongUserId, wrongResult], cancellationToken)
                .ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var management = new EntityFrameworkAuditManagement<TestAuditStoreDbContext>(context);

            var result = await management
                .QueryAsync(
                    new AuditFilter
                    {
                        CommandType = "Match.Command",
                        UserId = "user-1",
                        Result = AuditResult.Failure,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);

            _ = await Assert.That(result).HasSingleItem();
            _ = await Assert.That(result[0].Id).IsEqualTo(match.Id);
        }
    }

    [Test]
    public async Task QueryAsync_OrdersByOccurredAtDescending_AndRespectsSkipAndTake(
        CancellationToken cancellationToken
    )
    {
        var context = CreateContext(nameof(QueryAsync_OrdersByOccurredAtDescending_AndRespectsSkipAndTake));
        await using (context.ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;
            var records = new List<AuditRecord>();
            for (var i = 0; i < 5; i++)
            {
                records.Add(CreateRecord(now.AddMinutes(-i)));
            }

            await context.AuditEntries.AddRangeAsync(records, cancellationToken).ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var management = new EntityFrameworkAuditManagement<TestAuditStoreDbContext>(context);

            var result = await management
                .QueryAsync(new AuditFilter { Skip = 1, Take = 2 }, cancellationToken)
                .ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(result).HasCount(2);
                _ = await Assert.That(result[0].Id).IsEqualTo(records[1].Id);
                _ = await Assert.That(result[1].Id).IsEqualTo(records[2].Id);
            }
        }
    }

    [Test]
    public async Task GetStatisticsAsync_ReturnsCorrectSuccessAndFailureCounts(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(GetStatisticsAsync_ReturnsCorrectSuccessAndFailureCounts));
        await using (context.ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;
            var records = new[]
            {
                CreateRecord(now, result: AuditResult.Success),
                CreateRecord(now, result: AuditResult.Success),
                CreateRecord(now, result: AuditResult.Success),
                CreateRecord(now, result: AuditResult.Failure),
            };

            await context.AuditEntries.AddRangeAsync(records, cancellationToken).ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var management = new EntityFrameworkAuditManagement<TestAuditStoreDbContext>(context);

            var statistics = await management.GetStatisticsAsync(cancellationToken).ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(statistics.SuccessCount).IsEqualTo(3);
                _ = await Assert.That(statistics.FailureCount).IsEqualTo(1);
                _ = await Assert.That(statistics.TotalCount).IsEqualTo(4);
            }
        }
    }

    [Test]
    public async Task GetStatisticsAsync_WithNoFailureRecords_ReturnsZeroFailureCount(
        CancellationToken cancellationToken
    )
    {
        var context = CreateContext(nameof(GetStatisticsAsync_WithNoFailureRecords_ReturnsZeroFailureCount));
        await using (context.ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;
            var records = new[]
            {
                CreateRecord(now, result: AuditResult.Success),
                CreateRecord(now, result: AuditResult.Success),
            };

            await context.AuditEntries.AddRangeAsync(records, cancellationToken).ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var management = new EntityFrameworkAuditManagement<TestAuditStoreDbContext>(context);

            var statistics = await management.GetStatisticsAsync(cancellationToken).ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(statistics.SuccessCount).IsEqualTo(2);
                _ = await Assert.That(statistics.FailureCount).IsEqualTo(0);
            }
        }
    }

    [Test]
    public async Task GetStatisticsAsync_EmptyDatabase_ReturnsAllZero(CancellationToken cancellationToken)
    {
        var context = CreateContext(nameof(GetStatisticsAsync_EmptyDatabase_ReturnsAllZero));
        await using (context.ConfigureAwait(false))
        {
            var management = new EntityFrameworkAuditManagement<TestAuditStoreDbContext>(context);

            var statistics = await management.GetStatisticsAsync(cancellationToken).ConfigureAwait(false);

            _ = await Assert.That(statistics.TotalCount).IsEqualTo(0);
        }
    }
}
