namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using Microsoft.Data.SqlClient;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.Outbox;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;

/// <summary>
/// Runs the claim race against SQL Server with <c>READ_COMMITTED_SNAPSHOT</c> enabled, where the
/// competing poller reads row versions instead of blocking on the first poller's locks.
/// </summary>
[ClassDataSource<SqlServerDatabaseServiceFixture, EntityFrameworkOutboxInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("SqlServer")]
[TestGroup("EntityFramework")]
[InheritsTests]
public class SqlServerEntityFrameworkOutboxClaimRaceTests(
    IServiceFixture databaseServiceFixture,
    IServiceInitializer databaseInitializer
) : EntityFrameworkOutboxClaimRaceTestsBase(databaseServiceFixture, databaseInitializer)
{
    /// <inheritdoc />
    protected override async Task PrepareDatabaseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connectionString = DatabaseServiceFixture.ConnectionString;

        // ALTER DATABASE ... SET READ_COMMITTED_SNAPSHOT needs the database to itself, so drop the
        // pooled connections left behind by the schema creation first.
        var connection = new SqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            SqlConnection.ClearPool(connection);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = "ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE";
                _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            // Connections opened before the switch still run with the old setting.
            SqlConnection.ClearPool(connection);
        }
    }
}
