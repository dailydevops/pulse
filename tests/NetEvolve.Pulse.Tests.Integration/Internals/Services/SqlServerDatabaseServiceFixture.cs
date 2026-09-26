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

    // CREATE DATABASE copies the 'model' database and needs an exclusive lock on it. Concurrent
    // CREATE DATABASE statements against the shared container fail with error 1807
    // ("Could not obtain exclusive lock on database 'model'"), so creation is serialized per test session.
    private const int ModelLockErrorNumber = 1807;

    private static readonly SemaphoreSlim CreateDatabaseLock = new(1, 1);

    public async Task InitializeAsync()
    {
        await CreateDatabaseLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Bounded retry in case 'model' is briefly held by SQL Server itself (e.g. right after startup).
            for (var attempt = 1; attempt < 5; attempt++)
            {
                try
                {
                    await CreateDatabaseAsync().ConfigureAwait(false);
                    return;
                }
                catch (SqlException ex) when (ex.Number == ModelLockErrorNumber)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt)).ConfigureAwait(false);
                }
            }

            await CreateDatabaseAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to create SQL Server test database '{DatabaseName}'.", ex);
        }
        finally
        {
            _ = CreateDatabaseLock.Release();
        }
    }

    private async Task CreateDatabaseAsync()
    {
        var con = new SqlConnection(Container.ConnectionString);
        await using (con.ConfigureAwait(false))
        {
            await con.OpenAsync().ConfigureAwait(false);

            var cmd = con.CreateCommand();
            await using (cmd.ConfigureAwait(false))
            {
#pragma warning disable CA2100, S2077 // Review SQL queries for security vulnerabilities; DatabaseName is test-controlled, not user input
                cmd.CommandText = $"CREATE DATABASE [{DatabaseName}]";
#pragma warning restore CA2100, S2077

                _ = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
    }
}
