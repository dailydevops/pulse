namespace NetEvolve.Pulse.Tests.Unit.SQLite;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Idempotency;
using TUnit.Core;

/// <summary>
/// Database-focused tests for <see cref="SQLiteIdempotencyKeyRepository"/> using an in-memory SQLite database.
/// </summary>
[TestGroup("SQLite")]
public sealed class SQLiteIdempotencyKeyRepositoryDatabaseTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _keepAlive;
    private readonly string _connectionString;

    public SQLiteIdempotencyKeyRepositoryDatabaseTests()
    {
        _connectionString = $"Data Source=idempotency_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();

        using var cmd = new SqliteCommand(
            """
            CREATE TABLE IF NOT EXISTS "IdempotencyKey"
            (
                "IdempotencyKey" TEXT NOT NULL,
                "CreatedAt"      TEXT NOT NULL,
                CONSTRAINT "PK_IdempotencyKey" PRIMARY KEY ("IdempotencyKey")
            );
            """,
            _keepAlive
        );
        _ = cmd.ExecuteNonQuery();
    }

    public async ValueTask DisposeAsync() => await _keepAlive.DisposeAsync().ConfigureAwait(false);

    private SQLiteIdempotencyKeyRepository CreateRepository(bool enableWal = false) =>
        new(
            Options.Create(
                new IdempotencyKeyOptions { ConnectionString = _connectionString, EnableWalMode = enableWal }
            )
        );

    private async Task<string?> GetCreatedAtAsync(string key, CancellationToken cancellationToken)
    {
        var cmd = new SqliteCommand(
            "SELECT \"CreatedAt\" FROM \"IdempotencyKey\" WHERE \"IdempotencyKey\" = @k",
            _keepAlive
        );
        await using (cmd.ConfigureAwait(false))
        {
            _ = cmd.Parameters.AddWithValue("@k", key);
            return (string?)await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task ExistsAsync_WithUnknownKey_ReturnsFalse(CancellationToken cancellationToken)
    {
        var repository = CreateRepository();

        var exists = await repository
            .ExistsAsync("missing", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(exists).IsFalse();
    }

    [Test]
    public async Task StoreAsync_ThenExistsAsync_ReturnsTrue(CancellationToken cancellationToken)
    {
        var repository = CreateRepository(enableWal: true);

        await repository.StoreAsync("key-1", Now, cancellationToken).ConfigureAwait(false);
        var exists = await repository.ExistsAsync("key-1", cancellationToken: cancellationToken).ConfigureAwait(false);
        var other = await repository.ExistsAsync("key-2", cancellationToken: cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(exists).IsTrue();
            _ = await Assert.That(other).IsFalse();
        }
    }

    [Test]
    public async Task StoreAsync_PersistsCreatedAtAsUtcRoundTripString(CancellationToken cancellationToken)
    {
        var repository = CreateRepository();

        await repository
            .StoreAsync("key-1", new DateTimeOffset(2026, 3, 1, 15, 0, 0, TimeSpan.FromHours(5)), cancellationToken)
            .ConfigureAwait(false);

        var stored = await GetCreatedAtAsync("key-1", cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(stored).IsEqualTo(Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Test]
    public async Task StoreAsync_WithExistingKey_KeepsOriginalCreatedAt(CancellationToken cancellationToken)
    {
        var repository = CreateRepository();

        await repository.StoreAsync("key-1", Now, cancellationToken).ConfigureAwait(false);
        await repository.StoreAsync("key-1", Now.AddHours(1), cancellationToken).ConfigureAwait(false);

        var stored = await GetCreatedAtAsync("key-1", cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(stored).IsEqualTo(Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Test]
    public async Task ExistsAsync_WithValidFrom_ComparesCreatedAtInclusively(CancellationToken cancellationToken)
    {
        var repository = CreateRepository();
        await repository.StoreAsync("key-1", Now, cancellationToken).ConfigureAwait(false);

        var atBoundary = await repository.ExistsAsync("key-1", Now, cancellationToken).ConfigureAwait(false);
        var afterBoundary = await repository
            .ExistsAsync("key-1", Now.AddSeconds(1), cancellationToken)
            .ConfigureAwait(false);
        var beforeBoundary = await repository
            .ExistsAsync("key-1", Now.AddSeconds(-1), cancellationToken)
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(atBoundary).IsTrue();
            _ = await Assert.That(afterBoundary).IsFalse();
            _ = await Assert.That(beforeBoundary).IsTrue();
        }
    }

    [Test]
    public async Task ExistsAsync_WithValidFromInOtherOffset_NormalizesToUtc(CancellationToken cancellationToken)
    {
        var repository = CreateRepository();
        await repository.StoreAsync("key-1", Now, cancellationToken).ConfigureAwait(false);

        var exists = await repository
            .ExistsAsync("key-1", Now.ToOffset(TimeSpan.FromHours(5)), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(exists).IsTrue();
    }

    [Test]
    public async Task ExistsAsync_WithUnknownKeyAndValidFrom_ReturnsFalse(CancellationToken cancellationToken)
    {
        var repository = CreateRepository();

        var exists = await repository.ExistsAsync("missing", Now, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(exists).IsFalse();
    }

    [Test]
    public async Task ExistsAsync_ComparesLegacyNonUtcRowsByInstant(CancellationToken cancellationToken)
    {
        // Rows written by older versions may carry a non-UTC offset; they are compared via julianday().
        var cmd = new SqliteCommand(
            "INSERT INTO \"IdempotencyKey\" (\"IdempotencyKey\", \"CreatedAt\") VALUES ('legacy', '2026-03-01T15:00:00.0000000+05:00')",
            _keepAlive
        );
        await using (cmd.ConfigureAwait(false))
        {
            _ = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var repository = CreateRepository();

        var atInstant = await repository.ExistsAsync("legacy", Now, cancellationToken).ConfigureAwait(false);
        var afterInstant = await repository
            .ExistsAsync("legacy", Now.AddMinutes(1), cancellationToken)
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(atInstant).IsTrue();
            _ = await Assert.That(afterInstant).IsFalse();
        }
    }

    [Test]
    public async Task TryReserveAsync_WithNewKey_ReturnsTrueAndStoresCreatedAt(CancellationToken cancellationToken)
    {
        var repository = CreateRepository();

        var reserved = await repository
            .TryReserveAsync("key-1", Now, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var stored = await GetCreatedAtAsync("key-1", cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(reserved).IsTrue();
            _ = await Assert
                .That(stored)
                .IsEqualTo(Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [Test]
    public async Task TryReserveAsync_WithExistingKeyAndNoValidFrom_ReturnsFalse(CancellationToken cancellationToken)
    {
        var repository = CreateRepository();
        await repository.StoreAsync("key-1", Now, cancellationToken).ConfigureAwait(false);

        var reserved = await repository
            .TryReserveAsync("key-1", Now.AddHours(1), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var stored = await GetCreatedAtAsync("key-1", cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(reserved).IsFalse();
            _ = await Assert
                .That(stored)
                .IsEqualTo(Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [Test]
    public async Task TryReserveAsync_WithExistingKeyStillValid_ReturnsFalse(CancellationToken cancellationToken)
    {
        var repository = CreateRepository();
        await repository.StoreAsync("key-1", Now, cancellationToken).ConfigureAwait(false);

        // validFrom is older than the stored CreatedAt, so the key has not expired yet.
        var reserved = await repository
            .TryReserveAsync("key-1", Now.AddHours(1), Now.AddMinutes(-1), cancellationToken)
            .ConfigureAwait(false);

        _ = await Assert.That(reserved).IsFalse();
    }

    [Test]
    public async Task TryReserveAsync_WithExpiredKey_ReservesAgainAndRefreshesCreatedAt(
        CancellationToken cancellationToken
    )
    {
        var repository = CreateRepository();
        await repository.StoreAsync("key-1", Now, cancellationToken).ConfigureAwait(false);
        var newCreatedAt = Now.AddHours(2);

        // validFrom is newer than the stored CreatedAt, so the stored key counts as expired.
        var reserved = await repository
            .TryReserveAsync("key-1", newCreatedAt, Now.AddHours(1), cancellationToken)
            .ConfigureAwait(false);
        var stored = await GetCreatedAtAsync("key-1", cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(reserved).IsTrue();
            _ = await Assert
                .That(stored)
                .IsEqualTo(newCreatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [Test]
    public async Task TryReserveAsync_AfterReserve_SecondReserveReturnsFalse(CancellationToken cancellationToken)
    {
        var repository = CreateRepository();

        var first = await repository
            .TryReserveAsync("key-1", Now, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var second = await repository
            .TryReserveAsync("key-1", Now, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(first).IsTrue();
            _ = await Assert.That(second).IsFalse();
        }
    }

    [Test]
    public async Task Methods_WithInvalidKey_ThrowArgumentException(CancellationToken cancellationToken)
    {
        var repository = CreateRepository();

        using (Assert.Multiple())
        {
            _ = await Assert
                .That(async () =>
                    await repository.ExistsAsync(" ", cancellationToken: cancellationToken).ConfigureAwait(false)
                )
                .Throws<ArgumentException>();
            _ = await Assert
                .That(async () => await repository.StoreAsync(" ", Now, cancellationToken).ConfigureAwait(false))
                .Throws<ArgumentException>();
            _ = await Assert
                .That(async () =>
                    await repository
                        .TryReserveAsync(" ", Now, cancellationToken: cancellationToken)
                        .ConfigureAwait(false)
                )
                .Throws<ArgumentException>();
        }
    }

    [Test]
    public async Task Methods_WithCancelledToken_ThrowOperationCanceledException()
    {
        var repository = CreateRepository();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert
                .That(async () => await repository.ExistsAsync("k", cancellationToken: cts.Token).ConfigureAwait(false))
                .Throws<OperationCanceledException>();
            _ = await Assert
                .That(async () => await repository.StoreAsync("k", Now, cts.Token).ConfigureAwait(false))
                .Throws<OperationCanceledException>();
            _ = await Assert
                .That(async () =>
                    await repository.TryReserveAsync("k", Now, cancellationToken: cts.Token).ConfigureAwait(false)
                )
                .Throws<OperationCanceledException>();
        }
    }
}
