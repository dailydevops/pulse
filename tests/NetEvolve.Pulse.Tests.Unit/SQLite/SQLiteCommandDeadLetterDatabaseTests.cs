namespace NetEvolve.Pulse.Tests.Unit.SQLite;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.DeadLetter;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.DeadLetter;
using TUnit.Assertions.Enums;
using TUnit.Core;
using TUnit.Mocks;

/// <summary>
/// Database-focused tests for <see cref="SQLiteCommandDeadLetterStore"/> and
/// <see cref="SQLiteCommandDeadLetterManagement"/> using an in-memory SQLite database.
/// </summary>
[TestGroup("SQLite")]
public sealed class SQLiteCommandDeadLetterDatabaseTests : IAsyncDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly string _connectionString;
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero));

    public SQLiteCommandDeadLetterDatabaseTests()
    {
        _connectionString = $"Data Source=deadletter_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();

        using var cmd = new SqliteCommand(
            """
            CREATE TABLE IF NOT EXISTS "CommandDeadLetter"
            (
                "Id"               TEXT    NOT NULL,
                "CommandType"      TEXT    NOT NULL,
                "Payload"          TEXT    NOT NULL,
                "ExceptionType"    TEXT    NULL,
                "ExceptionMessage" TEXT    NULL,
                "OccurredAt"       TEXT    NOT NULL,
                "AttemptCount"     INTEGER NOT NULL DEFAULT 1,
                "Status"           INTEGER NOT NULL DEFAULT 0,
                CONSTRAINT "PK_CommandDeadLetter" PRIMARY KEY ("Id")
            );
            """,
            _keepAlive
        );
        _ = cmd.ExecuteNonQuery();
    }

    public async ValueTask DisposeAsync() => await _keepAlive.DisposeAsync().ConfigureAwait(false);

    private IOptions<CommandDeadLetterOptions> CreateOptions(bool enableWal = false) =>
        Options.Create(
            new CommandDeadLetterOptions { ConnectionString = _connectionString, EnableWalMode = enableWal }
        );

    private SQLiteCommandDeadLetterStore CreateStore(bool enableWal = false) =>
        new(CreateOptions(enableWal), _timeProvider);

    private SQLiteCommandDeadLetterManagement CreateManagement(bool enableWal = false) =>
        new(
            CreateOptions(enableWal),
            Mock.Of<IMediatorSendOnly>().Object,
            Mock.Of<IPayloadSerializer>().Object,
            _timeProvider
        );

    /// <summary>Stores a dead letter and returns the identifier the store generated for it.</summary>
    private async Task<Guid> StoreAsync(
        SQLiteCommandDeadLetterStore store,
        string commandType,
        CancellationToken cancellationToken,
        string payload = "{}",
        Exception? exception = null
    )
    {
        var known = await GetAllIdsAsync(cancellationToken).ConfigureAwait(false);
        await store
            .StoreAsync(commandType, payload, exception ?? new InvalidOperationException("boom"), cancellationToken)
            .ConfigureAwait(false);
        var after = await GetAllIdsAsync(cancellationToken).ConfigureAwait(false);
        return after.Except(known).Single();
    }

    private async Task<Guid[]> GetAllIdsAsync(CancellationToken cancellationToken)
    {
        var ids = new System.Collections.Generic.List<Guid>();
        var cmd = new SqliteCommand("SELECT \"Id\" FROM \"CommandDeadLetter\"", _keepAlive);
        await using (cmd.ConfigureAwait(false))
        {
            var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    ids.Add(Guid.Parse(reader.GetString(0)));
                }
            }
        }

        return [.. ids];
    }

    private async Task SetStatusDirectAsync(
        Guid id,
        CommandDeadLetterStatus status,
        CancellationToken cancellationToken
    )
    {
        var cmd = new SqliteCommand("UPDATE \"CommandDeadLetter\" SET \"Status\" = @s WHERE \"Id\" = @id", _keepAlive);
        await using (cmd.ConfigureAwait(false))
        {
            _ = cmd.Parameters.AddWithValue("@s", (int)status);
            _ = cmd.Parameters.AddWithValue("@id", id.ToString());
            _ = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task StoreAsync_PersistsEntryWithNewStatusAndSingleAttempt(CancellationToken cancellationToken)
    {
        var store = CreateStore(enableWal: true);
        var management = CreateManagement(enableWal: true);

        var id = await StoreAsync(store, "My.Command", cancellationToken, payload: "{\"x\":1}").ConfigureAwait(false);
        var entry = await management.GetEntryAsync(id, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(entry).IsNotNull();
            _ = await Assert.That(entry!.Id).IsEqualTo(id);
            _ = await Assert.That(entry.CommandType).IsEqualTo("My.Command");
            _ = await Assert.That(entry.Payload).IsEqualTo("{\"x\":1}");
            _ = await Assert
                .That(entry.ExceptionType)
                .IsEqualTo(typeof(InvalidOperationException).AssemblyQualifiedName);
            _ = await Assert.That(entry.ExceptionMessage).IsEqualTo("boom");
            _ = await Assert.That(entry.OccurredAt.UtcDateTime).IsEqualTo(_timeProvider.GetUtcNow().UtcDateTime);
            _ = await Assert.That(entry.AttemptCount).IsEqualTo(1);
            _ = await Assert.That(entry.Status).IsEqualTo(CommandDeadLetterStatus.New);
        }
    }

    [Test]
    public async Task StoreAsync_WithInvalidArguments_Throws(CancellationToken cancellationToken)
    {
        var store = CreateStore();
        var exception = new InvalidOperationException();

        using (Assert.Multiple())
        {
            _ = await Assert
                .That(async () => await store.StoreAsync(" ", "{}", exception, cancellationToken).ConfigureAwait(false))
                .Throws<ArgumentException>();
            _ = await Assert
                .That(async () => await store.StoreAsync("T", " ", exception, cancellationToken).ConfigureAwait(false))
                .Throws<ArgumentException>();
            _ = await Assert
                .That(async () => await store.StoreAsync("T", "{}", null!, cancellationToken).ConfigureAwait(false))
                .Throws<ArgumentNullException>();
        }
    }

    [Test]
    public async Task StoreAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        var store = CreateStore();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(false);

        _ = await Assert
            .That(async () =>
                await store.StoreAsync("T", "{}", new InvalidOperationException(), cts.Token).ConfigureAwait(false)
            )
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task GetEntryAsync_WithUnknownId_ReturnsNull(CancellationToken cancellationToken)
    {
        var management = CreateManagement();

        var entry = await management.GetEntryAsync(Guid.NewGuid(), cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(entry).IsNull();
    }

    [Test]
    public async Task GetPendingAsync_ReturnsOnlyNewEntriesOldestFirst(CancellationToken cancellationToken)
    {
        var store = CreateStore();
        var management = CreateManagement();
        var first = await StoreAsync(store, "A", cancellationToken).ConfigureAwait(false);
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        var second = await StoreAsync(store, "B", cancellationToken).ConfigureAwait(false);
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        var third = await StoreAsync(store, "C", cancellationToken).ConfigureAwait(false);
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        var resolved = await StoreAsync(store, "D", cancellationToken).ConfigureAwait(false);
        await SetStatusDirectAsync(resolved, CommandDeadLetterStatus.Resolved, cancellationToken).ConfigureAwait(false);

        var pending = await management.GetPendingAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        _ = await Assert
            .That(pending.Select(e => e.Id).ToArray())
            .IsEquivalentTo([first, second, third], CollectionOrdering.Matching);
    }

    [Test]
    public async Task GetPendingAsync_AppliesCountAndSkip(CancellationToken cancellationToken)
    {
        var store = CreateStore();
        var management = CreateManagement();
        var ids = new Guid[4];
        for (var i = 0; i < ids.Length; i++)
        {
            ids[i] = await StoreAsync(store, "A", cancellationToken).ConfigureAwait(false);
            _timeProvider.Advance(TimeSpan.FromMinutes(1));
        }

        var page = await management.GetPendingAsync(2, 1, cancellationToken).ConfigureAwait(false);

        _ = await Assert
            .That(page.Select(e => e.Id).ToArray())
            .IsEquivalentTo([ids[1], ids[2]], CollectionOrdering.Matching);
    }

    [Test]
    public async Task GetPendingAsync_WithInvalidArguments_ThrowsArgumentOutOfRangeException(
        CancellationToken cancellationToken
    )
    {
        var management = CreateManagement();

        using (Assert.Multiple())
        {
            _ = await Assert
                .That(async () => await management.GetPendingAsync(0, 0, cancellationToken).ConfigureAwait(false))
                .Throws<ArgumentOutOfRangeException>();
            _ = await Assert
                .That(async () => await management.GetPendingAsync(1, -1, cancellationToken).ConfigureAwait(false))
                .Throws<ArgumentOutOfRangeException>();
        }
    }

    [Test]
    public async Task DismissAsync_SetsStatusDismissed_AndExcludesFromPending(CancellationToken cancellationToken)
    {
        var store = CreateStore();
        var management = CreateManagement();
        var id = await StoreAsync(store, "A", cancellationToken).ConfigureAwait(false);

        await management.DismissAsync(id, cancellationToken).ConfigureAwait(false);
        var entry = await management.GetEntryAsync(id, cancellationToken).ConfigureAwait(false);
        var pending = await management.GetPendingAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(entry!.Status).IsEqualTo(CommandDeadLetterStatus.Dismissed);
            _ = await Assert.That(pending.Count).IsEqualTo(0);
        }
    }

    [Test]
    public async Task DismissAsync_WithUnknownId_ThrowsEntryNotFoundException(CancellationToken cancellationToken)
    {
        var management = CreateManagement();

        _ = await Assert
            .That(async () => await management.DismissAsync(Guid.NewGuid(), cancellationToken).ConfigureAwait(false))
            .Throws<CommandDeadLetterEntryNotFoundException>();
    }

    [Test]
    public async Task GetStatisticsAsync_WithEmptyTable_ReturnsZeros(CancellationToken cancellationToken)
    {
        var management = CreateManagement();

        var stats = await management.GetStatisticsAsync(cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(stats.NewCount).IsEqualTo(0);
            _ = await Assert.That(stats.ReplayingCount).IsEqualTo(0);
            _ = await Assert.That(stats.ResolvedCount).IsEqualTo(0);
            _ = await Assert.That(stats.DismissedCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task GetStatisticsAsync_CountsEveryStatus(CancellationToken cancellationToken)
    {
        var store = CreateStore();
        var management = CreateManagement();
        var newIds = new[]
        {
            await StoreAsync(store, "A", cancellationToken).ConfigureAwait(false),
            await StoreAsync(store, "A", cancellationToken).ConfigureAwait(false),
            await StoreAsync(store, "A", cancellationToken).ConfigureAwait(false),
            await StoreAsync(store, "A", cancellationToken).ConfigureAwait(false),
            await StoreAsync(store, "A", cancellationToken).ConfigureAwait(false),
            await StoreAsync(store, "A", cancellationToken).ConfigureAwait(false),
            await StoreAsync(store, "A", cancellationToken).ConfigureAwait(false),
        };
        await SetStatusDirectAsync(newIds[1], CommandDeadLetterStatus.Replaying, cancellationToken)
            .ConfigureAwait(false);
        await SetStatusDirectAsync(newIds[2], CommandDeadLetterStatus.Resolved, cancellationToken)
            .ConfigureAwait(false);
        await SetStatusDirectAsync(newIds[3], CommandDeadLetterStatus.Resolved, cancellationToken)
            .ConfigureAwait(false);
        await SetStatusDirectAsync(newIds[4], CommandDeadLetterStatus.Dismissed, cancellationToken)
            .ConfigureAwait(false);
        await SetStatusDirectAsync(newIds[5], CommandDeadLetterStatus.Dismissed, cancellationToken)
            .ConfigureAwait(false);
        await SetStatusDirectAsync(newIds[6], CommandDeadLetterStatus.Dismissed, cancellationToken)
            .ConfigureAwait(false);

        var stats = await management.GetStatisticsAsync(cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(stats.NewCount).IsEqualTo(1);
            _ = await Assert.That(stats.ReplayingCount).IsEqualTo(1);
            _ = await Assert.That(stats.ResolvedCount).IsEqualTo(2);
            _ = await Assert.That(stats.DismissedCount).IsEqualTo(3);
        }
    }

    [Test]
    public async Task ReplayAsync_WithUnknownId_ThrowsEntryNotFoundException(CancellationToken cancellationToken)
    {
        var management = CreateManagement();

        _ = await Assert
            .That(async () => await management.ReplayAsync(Guid.NewGuid(), cancellationToken).ConfigureAwait(false))
            .Throws<CommandDeadLetterEntryNotFoundException>();
    }

    [Test]
    public async Task ReplayAsync_WithDismissedEntry_ThrowsDismissedExceptionAndKeepsStatus(
        CancellationToken cancellationToken
    )
    {
        var store = CreateStore();
        var management = CreateManagement();
        var id = await StoreAsync(store, "A", cancellationToken).ConfigureAwait(false);
        await management.DismissAsync(id, cancellationToken).ConfigureAwait(false);

        _ = await Assert
            .That(async () => await management.ReplayAsync(id, cancellationToken).ConfigureAwait(false))
            .Throws<CommandDeadLetterEntryDismissedException>();

        var entry = await management.GetEntryAsync(id, cancellationToken).ConfigureAwait(false);
        _ = await Assert.That(entry!.Status).IsEqualTo(CommandDeadLetterStatus.Dismissed);
    }

    [Test]
    public async Task ReplayAsync_WhenReplayFails_ResetsEntryToNewAndIncrementsAttemptCount(
        CancellationToken cancellationToken
    )
    {
        var store = CreateStore();
        var management = CreateManagement();
        var id = await StoreAsync(store, "Not.A.Resolvable.CommandType", cancellationToken).ConfigureAwait(false);
        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        _ = await Assert
            .That(async () => await management.ReplayAsync(id, cancellationToken).ConfigureAwait(false))
            .Throws<InvalidOperationException>();

        var entry = await management.GetEntryAsync(id, cancellationToken).ConfigureAwait(false);
        using (Assert.Multiple())
        {
            _ = await Assert.That(entry!.Status).IsEqualTo(CommandDeadLetterStatus.New);
            _ = await Assert.That(entry.AttemptCount).IsEqualTo(2);
            _ = await Assert
                .That(entry.ExceptionType)
                .IsEqualTo(typeof(InvalidOperationException).AssemblyQualifiedName);
            _ = await Assert.That(entry.ExceptionMessage).IsNotEqualTo("boom");
            _ = await Assert.That(entry.OccurredAt.UtcDateTime).IsEqualTo(_timeProvider.GetUtcNow().UtcDateTime);
        }
    }

    [Test]
    public async Task ReplayAsync_WithSuccessfulReplay_DispatchesCommandAndMarksEntryResolved(
        CancellationToken cancellationToken
    )
    {
        var handler = new ReplayCommandHandler();
        var (provider, management, id) = await CreateReplayableEntryAsync(handler, cancellationToken)
            .ConfigureAwait(false);
        await using (provider.ConfigureAwait(false))
        {
            await management.ReplayAsync(id, cancellationToken).ConfigureAwait(false);

            var entry = await management.GetEntryAsync(id, cancellationToken).ConfigureAwait(false);
            using (Assert.Multiple())
            {
                _ = await Assert.That(handler.LastValue).IsEqualTo("replayed");
                _ = await Assert.That(entry!.Status).IsEqualTo(CommandDeadLetterStatus.Resolved);
                _ = await Assert.That(entry.AttemptCount).IsEqualTo(1);
            }
        }
    }

    [Test]
    public async Task ReplayAsync_WhenHandlerThrows_RethrowsAndResetsEntryWithHandlerException(
        CancellationToken cancellationToken
    )
    {
        var handler = new ReplayCommandHandler { Failure = new ArgumentException("handler failed") };
        var (provider, management, id) = await CreateReplayableEntryAsync(handler, cancellationToken)
            .ConfigureAwait(false);
        await using (provider.ConfigureAwait(false))
        {
            _timeProvider.Advance(TimeSpan.FromMinutes(5));

            _ = await Assert
                .That(async () => await management.ReplayAsync(id, cancellationToken).ConfigureAwait(false))
                .Throws<ArgumentException>();

            var entry = await management.GetEntryAsync(id, cancellationToken).ConfigureAwait(false);
            using (Assert.Multiple())
            {
                _ = await Assert.That(entry!.Status).IsEqualTo(CommandDeadLetterStatus.New);
                _ = await Assert.That(entry.AttemptCount).IsEqualTo(2);
                _ = await Assert.That(entry.ExceptionType).IsEqualTo(typeof(ArgumentException).AssemblyQualifiedName);
                _ = await Assert.That(entry.ExceptionMessage).IsEqualTo("handler failed");
                _ = await Assert.That(entry.OccurredAt.UtcDateTime).IsEqualTo(_timeProvider.GetUtcNow().UtcDateTime);
            }
        }
    }

    [Test]
    public async Task GetEntryAsync_WithNullExceptionColumns_ReturnsNullExceptionDetails(
        CancellationToken cancellationToken
    )
    {
        var management = CreateManagement();
        var id = Guid.NewGuid();
        var cmd = new SqliteCommand(
            "INSERT INTO \"CommandDeadLetter\" (\"Id\",\"CommandType\",\"Payload\",\"OccurredAt\") VALUES (@id,'T','{}',@at)",
            _keepAlive
        );
        await using (cmd.ConfigureAwait(false))
        {
            _ = cmd.Parameters.AddWithValue("@id", id.ToString());
            _ = cmd.Parameters.AddWithValue("@at", _timeProvider.GetUtcNow());
            _ = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var entry = await management.GetEntryAsync(id, cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(entry!.ExceptionType).IsNull();
            _ = await Assert.That(entry.ExceptionMessage).IsNull();
        }
    }

    private async Task<(
        ServiceProvider Provider,
        SQLiteCommandDeadLetterManagement Management,
        Guid Id
    )> CreateReplayableEntryAsync(ReplayCommandHandler handler, CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddPulse();
        _ = services.AddScoped<ICommandHandler<ReplayCommand, Extensibility.Void>>(_ => handler);
        var provider = services.BuildServiceProvider();

        var serializer = provider.GetRequiredService<IPayloadSerializer>();
        var management = new SQLiteCommandDeadLetterManagement(
            CreateOptions(),
            provider.GetRequiredService<IMediatorSendOnly>(),
            serializer,
            _timeProvider
        );

        var id = await StoreAsync(
                CreateStore(),
                typeof(ReplayCommand).AssemblyQualifiedName!,
                cancellationToken,
                serializer.Serialize(new ReplayCommand { Value = "replayed" })
            )
            .ConfigureAwait(false);

        return (provider, management, id);
    }

    private sealed class ReplayCommand : ICommand
    {
        public string? Value { get; set; }

        public string? CausationId { get; set; }

        public string? CorrelationId { get; set; }
    }

    private sealed class ReplayCommandHandler : ICommandHandler<ReplayCommand, Extensibility.Void>
    {
        public string? LastValue { get; private set; }

        public Exception? Failure { get; init; }

        public Task<Extensibility.Void> HandleAsync(
            ReplayCommand command,
            CancellationToken cancellationToken = default
        )
        {
            LastValue = command.Value;
            return Failure is null
                ? Task.FromResult(Extensibility.Void.Completed)
                : Task.FromException<Extensibility.Void>(Failure);
        }
    }
}
