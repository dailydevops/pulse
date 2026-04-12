namespace NetEvolve.Pulse.Tests.Integration.Internals;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.MsSql;

public sealed class SqlServerDatabaseServiceFixture : IDatabaseServiceFixture
{
    private readonly MsSqlContainer _container = new MsSqlBuilder(
        /*dockerimage*/"mcr.microsoft.com/mssql/server:2022-RTM-ubuntu-20.04"
    )
        .WithLogger(NullLogger.Instance)
        .Build();

    public string ConnectionString =>
        _container.GetConnectionString().Replace("master", DatabaseName, StringComparison.Ordinal);

    internal string DatabaseName { get; } = $"{TestHelper.TargetFramework}{Guid.NewGuid():N}";

    public DatabaseType DatabaseType => DatabaseType.SqlServer;

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync().WaitAsync(TimeSpan.FromMinutes(2));

            // Create temporary database to ensure the container is fully initialized and ready to accept connections
            await using var con = new SqlConnection(_container.GetConnectionString());
            await con.OpenAsync();

            await using var cmd = con.CreateCommand();
#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities
            cmd.CommandText = $"CREATE DATABASE [{DatabaseName}]";
#pragma warning restore CA2100 // Review SQL queries for security vulnerabilities

            _ = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
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
