namespace NetEvolve.Pulse.Tests.Integration.Internals;

using Microsoft.Data.SqlClient;

public sealed class SqlServerDatabaseServiceFixture : IServiceFixture
{
    [ClassDataSource<SqlServerContainerFixture>(Shared = SharedType.PerTestSession)]
    public SqlServerContainerFixture Container { get; set; } = default!;

    public string ConnectionString =>
        Container.ConnectionString.Replace("master", DatabaseName, StringComparison.Ordinal);

    internal string DatabaseName { get; } = $"{TestHelper.TargetFramework}{Guid.NewGuid():N}";

    public ServiceType ServiceType => ServiceType.SqlServer;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public async Task InitializeAsync()
    {
        try
        {
            // Create temporary database to ensure the container is fully initialized and ready to accept connections
            var con = new SqlConnection(Container.ConnectionString);
            // Create temporary database to ensure the container is fully initialized and ready to accept connections
            await using (con.ConfigureAwait(false))
            {
                await con.OpenAsync().ConfigureAwait(false);

                var cmd = con.CreateCommand();
                await using (cmd.ConfigureAwait(false))
                {
#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities
                    cmd.CommandText = $"CREATE DATABASE [{DatabaseName}]";
#pragma warning restore CA2100 // Review SQL queries for security vulnerabilities

                    _ = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "SQL Server container failed to start within the expected time frame. Try restarting Rancher Desktop.",
                ex
            );
        }
    }
}
