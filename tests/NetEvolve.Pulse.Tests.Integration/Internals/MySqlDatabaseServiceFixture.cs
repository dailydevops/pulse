namespace NetEvolve.Pulse.Tests.Integration.Internals;

using MySql.Data.MySqlClient;

/// <summary>
/// Provides a per-test <see cref="IServiceType"/> backed by a MySQL Testcontainer,
/// creating a unique database for each test to ensure isolation.
/// </summary>
public sealed class MySqlDatabaseServiceFixture : IServiceType
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
            await using var con = new MySqlConnection(Container.ConnectionString);
            await con.OpenAsync();

            await using var cmd = con.CreateCommand();
#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities
            cmd.CommandText = $"CREATE DATABASE {DatabaseName}";
#pragma warning restore CA2100 // Review SQL queries for security vulnerabilities

            _ = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
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
