namespace NetEvolve.Pulse.Tests.Unit.SQLite;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Audit;
using NetEvolve.Pulse.Extensibility.Audit;
using TUnit.Assertions.Enums;
using TUnit.Core;

/// <summary>
/// Database-focused tests for <see cref="SQLiteAuditStore"/> and <see cref="SQLiteAuditManagement"/>
/// using an in-memory SQLite database.
/// </summary>
[TestGroup("SQLite")]
public sealed class SQLiteAuditDatabaseTests : IAsyncDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly string _connectionString;

    public SQLiteAuditDatabaseTests()
    {
        _connectionString = $"Data Source=audit_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();

        using var cmd = new SqliteCommand(
            """
            CREATE TABLE IF NOT EXISTS "AuditEntry"
            (
                "Id"               TEXT    NOT NULL,
                "CommandType"      TEXT    NOT NULL,
                "UserId"           TEXT    NULL,
                "CorrelationId"    TEXT    NULL,
                "OccurredAt"       TEXT    NOT NULL,
                "DurationMs"       REAL    NOT NULL,
                "Result"           INTEGER NOT NULL,
                "Payload"          TEXT    NULL,
                "ExceptionMessage" TEXT    NULL,
                CONSTRAINT "PK_AuditEntry" PRIMARY KEY ("Id")
            );
            """,
            _keepAlive
        );
        _ = cmd.ExecuteNonQuery();
    }

    public async ValueTask DisposeAsync() => await _keepAlive.DisposeAsync().ConfigureAwait(false);

    private IOptions<AuditStoreOptions> CreateOptions(bool enableWal = false) =>
        Options.Create(new AuditStoreOptions { ConnectionString = _connectionString, EnableWalMode = enableWal });

    private SQLiteAuditStore CreateStore(bool enableWal = false) => new(CreateOptions(enableWal));

    private SQLiteAuditManagement CreateManagement(bool enableWal = false) => new(CreateOptions(enableWal));

    private static AuditRecord CreateRecord(
        string commandType = "TestCommand",
        string? userId = "user-1",
        DateTimeOffset? occurredAt = null,
        AuditResult result = AuditResult.Success,
        Guid? id = null
    ) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            CommandType = commandType,
            UserId = userId,
            CorrelationId = "corr-1",
            OccurredAt = occurredAt ?? DateTimeOffset.UtcNow,
            DurationMs = 12.5,
            Result = result,
            Payload = "{\"a\":1}",
            ExceptionMessage = result == AuditResult.Failure ? "boom" : null,
        };

    private static async Task RecordAllAsync(
        SQLiteAuditStore store,
        CancellationToken cancellationToken,
        params AuditRecord[] records
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var record in records)
        {
            await store.RecordAsync(record, cancellationToken).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task RecordAsync_WithNullRecord_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var store = CreateStore();

        _ = await Assert
            .That(async () => await store.RecordAsync(null!, cancellationToken).ConfigureAwait(false))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task RecordAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        var store = CreateStore();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(false);

        _ = await Assert
            .That(async () => await store.RecordAsync(CreateRecord(), cts.Token).ConfigureAwait(false))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task RecordAsync_PersistsAllFields_ReadBackByGetByIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var store = CreateStore(enableWal: true);
        var management = CreateManagement(enableWal: true);
        var record = CreateRecord(result: AuditResult.Failure);

        await store.RecordAsync(record, cancellationToken).ConfigureAwait(false);
        var loaded = await management.GetByIdAsync(record.Id, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(loaded).IsNotNull();
            _ = await Assert.That(loaded!.Id).IsEqualTo(record.Id);
            _ = await Assert.That(loaded.CommandType).IsEqualTo(record.CommandType);
            _ = await Assert.That(loaded.UserId).IsEqualTo(record.UserId);
            _ = await Assert.That(loaded.CorrelationId).IsEqualTo(record.CorrelationId);
            _ = await Assert.That(loaded.OccurredAt.UtcDateTime).IsEqualTo(record.OccurredAt.UtcDateTime);
            _ = await Assert.That(loaded.DurationMs).IsEqualTo(record.DurationMs);
            _ = await Assert.That(loaded.Result).IsEqualTo(AuditResult.Failure);
            _ = await Assert.That(loaded.Payload).IsEqualTo(record.Payload);
            _ = await Assert.That(loaded.ExceptionMessage).IsEqualTo("boom");
        }
    }

    [Test]
    public async Task RecordAsync_WithNullableFieldsNull_ReadsBackNulls(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var store = CreateStore();
        var management = CreateManagement();
        var record = CreateRecord(userId: null);
        record.CorrelationId = null;
        record.Payload = null;

        await store.RecordAsync(record, cancellationToken).ConfigureAwait(false);
        var loaded = await management.GetByIdAsync(record.Id, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(loaded!.UserId).IsNull();
            _ = await Assert.That(loaded.CorrelationId).IsNull();
            _ = await Assert.That(loaded.Payload).IsNull();
            _ = await Assert.That(loaded.ExceptionMessage).IsNull();
        }
    }

    [Test]
    public async Task RecordAsync_NormalizesOccurredAtToUtc(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var store = CreateStore();
        var management = CreateManagement();
        var record = CreateRecord(occurredAt: new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(5)));

        await store.RecordAsync(record, cancellationToken).ConfigureAwait(false);
        var loaded = await management.GetByIdAsync(record.Id, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(loaded!.OccurredAt.Offset).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task GetByIdAsync_WithUnknownId_ReturnsNull(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var management = CreateManagement();

        var loaded = await management.GetByIdAsync(Guid.NewGuid(), cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(loaded).IsNull();
    }

    [Test]
    public async Task GetByIdAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        var management = CreateManagement();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(false);

        _ = await Assert
            .That(async () => await management.GetByIdAsync(Guid.NewGuid(), cts.Token).ConfigureAwait(false))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task QueryAsync_WithoutFilter_ReturnsNewestFirstThenByIdDescending(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var store = CreateStore();
        var management = CreateManagement();
        var now = DateTimeOffset.UtcNow;
        var oldest = CreateRecord(occurredAt: now.AddMinutes(-10));
        var sameTimeLow = CreateRecord(occurredAt: now, id: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var sameTimeHigh = CreateRecord(occurredAt: now, id: Guid.Parse("00000000-0000-0000-0000-000000000002"));
        await RecordAllAsync(store, cancellationToken, oldest, sameTimeLow, sameTimeHigh).ConfigureAwait(false);

        var result = await management.QueryAsync(new AuditFilter(), cancellationToken).ConfigureAwait(false);

        _ = await Assert
            .That(result.Select(r => r.Id).ToArray())
            .IsEquivalentTo([sameTimeHigh.Id, sameTimeLow.Id, oldest.Id], CollectionOrdering.Matching);
    }

    [Test]
    public async Task QueryAsync_WithNullFilter_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var management = CreateManagement();

        _ = await Assert
            .That(async () => await management.QueryAsync(null!, cancellationToken).ConfigureAwait(false))
            .Throws<ArgumentNullException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task QueryAsync_WithNonPositiveTake_ThrowsArgumentOutOfRangeException(
        int take,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var management = CreateManagement();

        _ = await Assert
            .That(async () =>
                await management.QueryAsync(new AuditFilter { Take = take }, cancellationToken).ConfigureAwait(false)
            )
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task QueryAsync_WithNegativeSkip_ThrowsArgumentOutOfRangeException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var management = CreateManagement();

        _ = await Assert
            .That(async () =>
                await management.QueryAsync(new AuditFilter { Skip = -1 }, cancellationToken).ConfigureAwait(false)
            )
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task QueryAsync_WithTakeAndSkip_ReturnsRequestedPage(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var store = CreateStore();
        var management = CreateManagement();
        var now = DateTimeOffset.UtcNow;
        var records = Enumerable.Range(0, 5).Select(i => CreateRecord(occurredAt: now.AddMinutes(-i))).ToArray();
        await RecordAllAsync(store, cancellationToken, records).ConfigureAwait(false);

        var page = await management
            .QueryAsync(new AuditFilter { Take = 2, Skip = 1 }, cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert
            .That(page.Select(r => r.Id).ToArray())
            .IsEquivalentTo([records[1].Id, records[2].Id], CollectionOrdering.Matching);
    }

    [Test]
    public async Task QueryAsync_FiltersByCommandType(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var store = CreateStore();
        var management = CreateManagement();
        var match = CreateRecord(commandType: "A");
        await RecordAllAsync(store, cancellationToken, match, CreateRecord(commandType: "B")).ConfigureAwait(false);

        var result = await management
            .QueryAsync(new AuditFilter { CommandType = "A" }, cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(result.Select(r => r.Id).ToArray()).IsEquivalentTo([match.Id]);
    }

    [Test]
    public async Task QueryAsync_FiltersByUserId(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var store = CreateStore();
        var management = CreateManagement();
        var match = CreateRecord(userId: "alice");
        await RecordAllAsync(store, cancellationToken, match, CreateRecord(userId: "bob")).ConfigureAwait(false);

        var result = await management
            .QueryAsync(new AuditFilter { UserId = "alice" }, cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(result.Select(r => r.Id).ToArray()).IsEquivalentTo([match.Id]);
    }

    [Test]
    public async Task QueryAsync_FiltersByResult(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var store = CreateStore();
        var management = CreateManagement();
        var failure = CreateRecord(result: AuditResult.Failure);
        await RecordAllAsync(store, cancellationToken, failure, CreateRecord(result: AuditResult.Success))
            .ConfigureAwait(false);

        var result = await management
            .QueryAsync(new AuditFilter { Result = AuditResult.Failure }, cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(result.Select(r => r.Id).ToArray()).IsEquivalentTo([failure.Id]);
    }

    [Test]
    public async Task QueryAsync_FromAndTo_AreInclusiveBounds(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var store = CreateStore();
        var management = CreateManagement();
        var t0 = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
        var before = CreateRecord(occurredAt: t0.AddSeconds(-1));
        var atFrom = CreateRecord(occurredAt: t0);
        var atTo = CreateRecord(occurredAt: t0.AddMinutes(5));
        var after = CreateRecord(occurredAt: t0.AddMinutes(5).AddSeconds(1));
        await RecordAllAsync(store, cancellationToken, before, atFrom, atTo, after).ConfigureAwait(false);

        var result = await management
            .QueryAsync(new AuditFilter { From = t0, To = t0.AddMinutes(5) }, cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert
            .That(result.Select(r => r.Id).ToArray())
            .IsEquivalentTo([atTo.Id, atFrom.Id], CollectionOrdering.Matching);
    }

    [Test]
    public async Task QueryAsync_NormalizesNonUtcBounds(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var store = CreateStore();
        var management = CreateManagement();
        var instant = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
        var record = CreateRecord(occurredAt: instant);
        await store.RecordAsync(record, cancellationToken).ConfigureAwait(false);

        // The same instant expressed with a +05:00 offset must still match the inclusive bounds.
        var local = instant.ToOffset(TimeSpan.FromHours(5));
        var result = await management
            .QueryAsync(new AuditFilter { From = local, To = local }, cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(result.Select(r => r.Id).ToArray()).IsEquivalentTo([record.Id]);
    }

    [Test]
    public async Task QueryAsync_CombinesFiltersWithAnd(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var store = CreateStore();
        var management = CreateManagement();
        var t0 = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
        var match = CreateRecord("A", "alice", t0, AuditResult.Failure);
        await RecordAllAsync(
                store,
                cancellationToken,
                match,
                CreateRecord("B", "alice", t0, AuditResult.Failure),
                CreateRecord("A", "bob", t0, AuditResult.Failure),
                CreateRecord("A", "alice", t0, AuditResult.Success),
                CreateRecord("A", "alice", t0.AddDays(-1), AuditResult.Failure),
                CreateRecord("A", "alice", t0.AddDays(1), AuditResult.Failure)
            )
            .ConfigureAwait(false);

        var result = await management
            .QueryAsync(
                new AuditFilter
                {
                    CommandType = "A",
                    UserId = "alice",
                    Result = AuditResult.Failure,
                    From = t0.AddHours(-1),
                    To = t0.AddHours(1),
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(result.Select(r => r.Id).ToArray()).IsEquivalentTo([match.Id]);
    }

    [Test]
    public async Task GetStatisticsAsync_WithEmptyTable_ReturnsZeros(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var management = CreateManagement();

        var stats = await management.GetStatisticsAsync(cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(stats.SuccessCount).IsEqualTo(0);
            _ = await Assert.That(stats.FailureCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task GetStatisticsAsync_CountsSuccessAndFailure(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var store = CreateStore();
        var management = CreateManagement();
        await RecordAllAsync(
                store,
                cancellationToken,
                CreateRecord(result: AuditResult.Success),
                CreateRecord(result: AuditResult.Success),
                CreateRecord(result: AuditResult.Success),
                CreateRecord(result: AuditResult.Failure)
            )
            .ConfigureAwait(false);

        var stats = await management.GetStatisticsAsync(cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(stats.SuccessCount).IsEqualTo(3);
            _ = await Assert.That(stats.FailureCount).IsEqualTo(1);
            _ = await Assert.That(stats.TotalCount).IsEqualTo(4);
        }
    }

    [Test]
    public async Task GetStatisticsAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        var management = CreateManagement();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(false);

        _ = await Assert
            .That(async () => await management.GetStatisticsAsync(cts.Token).ConfigureAwait(false))
            .Throws<OperationCanceledException>();
    }
}
