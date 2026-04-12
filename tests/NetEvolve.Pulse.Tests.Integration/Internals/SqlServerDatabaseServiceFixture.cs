namespace NetEvolve.Pulse.Tests.Integration.Internals;

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
        _container
            .GetConnectionString()
            .Replace("master", $"{TestHelper.TargetFramework}{Guid.NewGuid():N}", StringComparison.Ordinal);

    public DatabaseType DatabaseType => DatabaseType.SqlServer;

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync().WaitAsync(TimeSpan.FromMinutes(2));
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
