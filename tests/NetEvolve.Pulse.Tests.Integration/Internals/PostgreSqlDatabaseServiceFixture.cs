namespace NetEvolve.Pulse.Tests.Integration.Internals;

public sealed class PostgreSqlDatabaseServiceFixture : IDatabaseServiceFixture
{
    [ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerTestSession)]
    public PostgreSqlContainerFixture Container { get; set; } = default!;

    public string ConnectionString =>
        Container.ConnectionString.Replace("Database=postgres;", $"Database={DatabaseName};", StringComparison.Ordinal);

    internal string DatabaseName { get; } = $"{TestHelper.TargetFramework}{Guid.NewGuid():N}";

    public DatabaseType DatabaseType => DatabaseType.PostgreSQL;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public async Task InitializeAsync()
    {
        try
        {
            // Create temporary database to ensure the container is fully initialized and ready to accept connections
            await using var con = new Npgsql.NpgsqlConnection(Container.ConnectionString);
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
