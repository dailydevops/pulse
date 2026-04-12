namespace NetEvolve.Pulse.Tests.Integration.Internals;

using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

public sealed class PostgreSqlDatabaseServiceFixture : IDatabaseServiceFixture
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(
        /*dockerimage*/"postgres:15.17"
    )
        .WithLogger(NullLogger.Instance)
        .WithDatabase($"{TestHelper.TargetFramework}{Guid.NewGuid():N}")
        .Build();

    public string ConnectionString => _container.GetConnectionString() + ";Include Error Detail=true;";

    public DatabaseType DatabaseType => DatabaseType.PostgreSQL;

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
                "PostgreSQL container failed to start within the expected time frame. Try restarting Rancher Desktop.",
                ex
            );
        }
    }
}
