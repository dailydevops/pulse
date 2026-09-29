namespace NetEvolve.Pulse.Tests.Integration.Idempotency;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Idempotency;
using NetEvolve.Pulse.Idempotency;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.Idempotency;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[ClassDataSource<SQLiteDatabaseServiceFixture, SQLiteAdoNetIdempotencyInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("SQLite")]
[TestGroup("AdoNet")]
[InheritsTests]
public class SQLiteAdoNetIdempotencyTests(
    IServiceFixture databaseServiceFixture,
    IServiceInitializer databaseInitializer
) : IdempotencyTestsBase(databaseServiceFixture, databaseInitializer)
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(2);

    [Test]
    public async Task Should_Treat_Offset_Key_Before_Cutoff_As_Absent(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var repository = services.GetRequiredService<IIdempotencyKeyRepository>();

                    // 13:00+02:00 is 11:00Z, one hour before the 12:00Z cutoff.
                    await repository
                        .StoreAsync("offset-key", TestDateTime.AddHours(-1).ToOffset(Offset), token)
                        .ConfigureAwait(false);

                    var exists = await repository.ExistsAsync("offset-key", TestDateTime, token).ConfigureAwait(false);

                    _ = await Assert.That(exists).IsFalse();
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_Treat_Key_After_Offset_Cutoff_As_Present(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var repository = services.GetRequiredService<IIdempotencyKeyRepository>();

                    await repository.StoreAsync("utc-key", TestDateTime, token).ConfigureAwait(false);

                    // 13:00+02:00 is 11:00Z, one hour before the key was created.
                    var exists = await repository
                        .ExistsAsync("utc-key", TestDateTime.AddHours(-1).ToOffset(Offset), token)
                        .ConfigureAwait(false);

                    _ = await Assert.That(exists).IsTrue();
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_Reserve_Offset_Key_Before_Cutoff(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var repository = services.GetRequiredService<IIdempotencyKeyRepository>();

                    await repository
                        .StoreAsync("offset-key", TestDateTime.AddHours(-1).ToOffset(Offset), token)
                        .ConfigureAwait(false);

                    var reserved = await repository
                        .TryReserveAsync("offset-key", TestDateTime.AddMinutes(30), TestDateTime, token)
                        .ConfigureAwait(false);

                    _ = await Assert.That(reserved).IsTrue();
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_Not_Reserve_Key_After_Offset_Cutoff(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var repository = services.GetRequiredService<IIdempotencyKeyRepository>();

                    await repository.StoreAsync("utc-key", TestDateTime, token).ConfigureAwait(false);

                    var reserved = await repository
                        .TryReserveAsync(
                            "utc-key",
                            TestDateTime.AddMinutes(30).ToOffset(Offset),
                            TestDateTime.AddHours(-1).ToOffset(Offset),
                            token
                        )
                        .ConfigureAwait(false);

                    _ = await Assert.That(reserved).IsFalse();
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_Compare_Legacy_Offset_Row_In_Utc(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var repository = services.GetRequiredService<IIdempotencyKeyRepository>();

                    // Rows written before the UTC normalization keep the caller's offset: 13:00+02:00 is 11:00Z.
                    await InsertRawRowAsync(services, "legacy-old", "2025-01-01T13:00:00.0000000+02:00", token)
                        .ConfigureAwait(false);
                    // 15:00+02:00 is 13:00Z, one hour after the 12:00Z cutoff.
                    await InsertRawRowAsync(services, "legacy-new", "2025-01-01T15:00:00.0000000+02:00", token)
                        .ConfigureAwait(false);

                    var oldExists = await repository
                        .ExistsAsync("legacy-old", TestDateTime, token)
                        .ConfigureAwait(false);
                    var newExists = await repository
                        .ExistsAsync("legacy-new", TestDateTime, token)
                        .ConfigureAwait(false);
                    var newReserved = await repository
                        .TryReserveAsync("legacy-new", TestDateTime.AddHours(2), TestDateTime, token)
                        .ConfigureAwait(false);
                    var oldReserved = await repository
                        .TryReserveAsync("legacy-old", TestDateTime.AddHours(2), TestDateTime, token)
                        .ConfigureAwait(false);

                    _ = await Assert.That(oldExists).IsFalse();
                    _ = await Assert.That(newExists).IsTrue();
                    _ = await Assert.That(newReserved).IsFalse();
                    _ = await Assert.That(oldReserved).IsTrue();
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    private static async Task InsertRawRowAsync(
        IServiceProvider services,
        string key,
        string createdAt,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = services.GetRequiredService<IOptions<IdempotencyKeyOptions>>().Value;

        var connection = new SqliteConnection(options.ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

#pragma warning disable CA2100, S2077 // Table name comes from the test method name, not user input.
            var command = new SqliteCommand(
                $"""INSERT INTO "{options.TableName}" ("IdempotencyKey", "CreatedAt") VALUES (@key, @createdAt);""",
                connection
            );
#pragma warning restore CA2100, S2077
            await using (command.ConfigureAwait(false))
            {
                _ = command.Parameters.AddWithValue("@key", key);
                _ = command.Parameters.AddWithValue("@createdAt", createdAt);
                _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
