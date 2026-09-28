namespace NetEvolve.Pulse.Tests.Integration.Internals.Services;

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

    private const int MaxAttempts = 5;

    private static readonly SemaphoreSlim CreateDatabaseLock = new(1, 1);

    // Set once connecting has failed MaxAttempts times. Creation is serialized, so without it every remaining fixture
    // would wait through the same connect timeouts against an unreachable server.
    private static Exception? _serverUnreachable;

    public async Task InitializeAsync()
    {
        await CreateDatabaseLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_serverUnreachable is not null)
            {
                throw new InvalidOperationException(
                    $"Failed to create SQL Server test database '{DatabaseName}': SQL Server was unreachable earlier in this test session.",
                    _serverUnreachable
                );
            }

            try
            {
                await CreateDatabaseAsync(Container.ConnectionString, DatabaseName).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var diagnostics = await Container.GetDiagnosticsAsync().ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"Failed to create SQL Server test database '{DatabaseName}'.{Environment.NewLine}{diagnostics}",
                    ex
                );
            }
        }
        finally
        {
            _ = CreateDatabaseLock.Release();
        }
    }

    private static async Task CreateDatabaseAsync(string connectionString, string databaseName)
    {
        // Bounded retry: the connect fails transiently (managed SNI error 35 on Linux, which SqlClient does not treat
        // as transient, see dotnet/SqlClient#4665), or 'model' is briefly held by SQL Server itself (e.g. right after
        // startup).
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var con = new SqlConnection(connectionString);
            await using (con.ConfigureAwait(false))
            {
                try
                {
                    await con.OpenAsync().ConfigureAwait(false);
                }
                catch (SqlException ex)
                {
                    if (attempt == MaxAttempts)
                    {
                        _serverUnreachable = ex;
                        throw;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(attempt)).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    var cmd = con.CreateCommand();
                    await using (cmd.ConfigureAwait(false))
                    {
#pragma warning disable CA2100, S2077 // Review SQL queries for security vulnerabilities; DatabaseName is test-controlled, not user input
                        cmd.CommandText = $"CREATE DATABASE [{databaseName}]";
#pragma warning restore CA2100, S2077

                        _ = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                        return;
                    }
                }
                catch (SqlException ex) when (ex.Number == ModelLockErrorNumber && attempt < MaxAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt)).ConfigureAwait(false);
                }
            }
        }
    }
}
