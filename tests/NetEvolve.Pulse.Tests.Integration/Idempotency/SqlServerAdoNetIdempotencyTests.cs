namespace NetEvolve.Pulse.Tests.Integration.Idempotency;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Idempotency;
using NetEvolve.Pulse.Idempotency;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.Idempotency;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;

[ClassDataSource<SqlServerDatabaseServiceFixture, SqlServerAdoNetIdempotencyInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("SqlServer")]
[TestGroup("AdoNet")]
[InheritsTests]
public class SqlServerAdoNetIdempotencyTests(
    IServiceFixture databaseServiceFixture,
    IServiceInitializer databaseInitializer
) : IdempotencyTestsBase(databaseServiceFixture, databaseInitializer)
{
    [Test]
    public async Task Should_Upgrade_Legacy_Key_Column_When_Script_Is_Rerun(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var options = services.GetRequiredService<IOptions<IdempotencyKeyOptions>>().Value;
                    var connectionString = options.ConnectionString!;
                    var table = $"[{options.Schema}].[{options.TableName}]";

                    // Recreate the table in the shape shipped before the key column was fixed:
                    // NVARCHAR(500) in the database default collation.
                    await ExecuteAsync(
                            connectionString,
                            $"""
                            DROP TABLE {table};
                            CREATE TABLE {table}
                            (
                                [IdempotencyKey] NVARCHAR(500) NOT NULL,
                                [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
                                CONSTRAINT [PK_{options.TableName}] PRIMARY KEY CLUSTERED ([IdempotencyKey])
                            );
                            CREATE NONCLUSTERED INDEX [IX_{options.TableName}_CreatedAt] ON {table} ([CreatedAt]);
                            INSERT INTO {table} ([IdempotencyKey], [CreatedAt]) VALUES (N'legacy-key', SYSDATETIMEOFFSET());
                            """,
                            token
                        )
                        .ConfigureAwait(false);

                    await DatabaseInitializer.CreateDatabaseAsync(services, token).ConfigureAwait(false);

                    var (maxLength, collation) = await GetKeyColumnAsync(connectionString, table, token)
                        .ConfigureAwait(false);
                    var store = services.GetRequiredService<IIdempotencyStore>();

                    using (Assert.Multiple())
                    {
                        _ = await Assert.That(maxLength).IsEqualTo(IdempotencyKeySchema.MaxLengths.IdempotencyKey * 2);
                        _ = await Assert.That(collation).IsEqualTo("Latin1_General_100_BIN2");
                        _ = await Assert
                            .That(await store.ExistsAsync("legacy-key", token).ConfigureAwait(false))
                            .IsTrue();
                        _ = await Assert
                            .That(await store.ExistsAsync("LEGACY-KEY", token).ConfigureAwait(false))
                            .IsFalse();
                    }
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_Roll_Back_And_Report_Failed_Upgrade_When_Primary_Key_Name_Differs(
        CancellationToken cancellationToken
    ) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var options = services.GetRequiredService<IOptions<IdempotencyKeyOptions>>().Value;
                    var connectionString = options.ConnectionString!;
                    var table = $"[{options.Schema}].[{options.TableName}]";

                    // A hand-managed legacy table whose primary key does not use the name the script expects.
                    await ExecuteAsync(
                            connectionString,
                            $"""
                            DROP TABLE {table};
                            CREATE TABLE {table}
                            (
                                [IdempotencyKey] NVARCHAR(500) NOT NULL,
                                [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
                                CONSTRAINT [PK_Custom_{options.TableName}] PRIMARY KEY CLUSTERED ([IdempotencyKey])
                            );
                            """,
                            token
                        )
                        .ConfigureAwait(false);

                    var exception = await Assert
                        .That(async () =>
                            await DatabaseInitializer.CreateDatabaseAsync(services, token).ConfigureAwait(false)
                        )
                        .Throws<SqlException>();

                    var (maxLength, _) = await GetKeyColumnAsync(connectionString, table, token).ConfigureAwait(false);

                    using (Assert.Multiple())
                    {
                        _ = await Assert.That(exception!.Number).IsEqualTo(50001);
                        _ = await Assert.That(maxLength).IsEqualTo(1000);
                    }
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    private static async Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = new SqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

#pragma warning disable CA2100 // sql is built from test-controlled schema and table names
            var command = new SqlCommand(sql, connection);
#pragma warning restore CA2100
            await using (command.ConfigureAwait(false))
            {
                _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<(int MaxLength, string? Collation)> GetKeyColumnAsync(
        string connectionString,
        string table,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = new SqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new SqlCommand(
                """
                SELECT [max_length], [collation_name]
                FROM sys.columns
                WHERE [object_id] = OBJECT_ID(@table) AND [name] = N'IdempotencyKey'
                """,
                connection
            );
            await using (command.ConfigureAwait(false))
            {
                _ = command.Parameters.AddWithValue("@table", table);

                var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    _ = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                    var collation = await reader.IsDBNullAsync(1, cancellationToken).ConfigureAwait(false)
                        ? null
                        : reader.GetString(1);
                    return (reader.GetInt16(0), collation);
                }
            }
        }
    }
}
