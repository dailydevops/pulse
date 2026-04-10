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

    public string ConnectionString => _container.GetConnectionString();

    public DatabaseType DatabaseType => DatabaseType.SqlServer;

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    public Task InitializeAsync() => _container.StartAsync();
}
