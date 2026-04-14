namespace NetEvolve.Pulse.Tests.Integration.Internals;

using Npgsql;

public sealed class PostgreSqlDatabaseServiceFixture : IDatabaseServiceFixture
{
    [ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerTestSession)]
    public PostgreSqlContainerFixture Container { get; set; } = default!;

    public string ConnectionString
    {
        get
        {
            // Use NpgsqlConnectionStringBuilder to safely set the test database name and
            // constrain the pool. Each test has a unique connection string, so without these
            // limits the per-test pools accumulate physical connections and exhaust
            // PostgreSQL's max_connections during parallel runs.
            var builder = new NpgsqlConnectionStringBuilder(Container.ConnectionString)
            {
                Database = DatabaseName,
                MinPoolSize = 0,
                MaxPoolSize = 5,
                ConnectionIdleLifetime = 15, // Must be >= ConnectionPruningInterval (default 10 s).
            };
            return builder.ToString();
        }
    }

    internal string DatabaseName { get; } = $"{TestHelper.TargetFramework}{Guid.NewGuid():N}";

    public DatabaseType DatabaseType => DatabaseType.PostgreSQL;

    public async ValueTask DisposeAsync()
    {
        // Eagerly clear the Npgsql pool for this test's unique connection string so that
        // physical connections are returned to the server immediately instead of sitting
        // idle for ConnectionIdleLifetime seconds.  Without this, completed-test pools
        // accumulate and exhaust PostgreSQL's max_connections during parallel runs.
        await using var conn = new NpgsqlConnection(ConnectionString);
        NpgsqlConnection.ClearPool(conn);
    }

    public async Task InitializeAsync()
    {
        try
        {
            // Create temporary database to ensure the container is fully initialized and ready to accept connections
            await using var con = new NpgsqlConnection(Container.ConnectionString);
            await con.OpenAsync();

            await using var cmd = con.CreateCommand();
#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities
            cmd.CommandText = $"CREATE DATABASE \"{DatabaseName}\"";
#pragma warning restore CA2100 // Review SQL queries for security vulnerabilities

            _ = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "PostgreSQL container failed to start within the expected time frame. Try restarting Rancher Desktop.",
                ex
            );
        }
    }
}
