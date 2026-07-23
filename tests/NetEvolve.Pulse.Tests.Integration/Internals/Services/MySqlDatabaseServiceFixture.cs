namespace NetEvolve.Pulse.Tests.Integration.Internals;

using MySql.Data.MySqlClient;

/// <summary>
/// Provides a per-test <see cref="IServiceFixture"/> backed by a MySQL Testcontainer,
/// creating a unique database for each test to ensure isolation.
/// </summary>
public sealed class MySqlDatabaseServiceFixture : IServiceFixture
{
    [ClassDataSource<MySqlContainerFixture>(Shared = SharedType.PerTestSession)]
    public MySqlContainerFixture Container { get; set; } = default!;

    public string ConnectionString =>
        Container.ConnectionString.Replace(";Database=test;", $";Database={DatabaseName};", StringComparison.Ordinal);

    internal string DatabaseName { get; } = $"{TestHelper.TargetFramework}{Guid.NewGuid():N}";

    public ServiceType ServiceType => ServiceType.MySql;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public async Task InitializeAsync()
    {
        try
        {
            // Create temporary database to ensure the container is fully initialized and ready to accept connections
            var con = new MySqlConnection(Container.ConnectionString);
            // Create temporary database to ensure the container is fully initialized and ready to accept connections
            await using (con.ConfigureAwait(false))
            {
                await con.OpenAsync().ConfigureAwait(false);

                var cmd = con.CreateCommand();
                await using (cmd.ConfigureAwait(false))
                {
#pragma warning disable CA2100, S2077 // Review SQL queries for security vulnerabilities; DatabaseName is test-controlled, not user input
                    cmd.CommandText = $"CREATE DATABASE {DatabaseName}";
#pragma warning restore CA2100, S2077

                    _ = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "MySQL container failed to start within the expected time frame. Try restarting Rancher Desktop.",
                ex
            );
        }
    }
}
