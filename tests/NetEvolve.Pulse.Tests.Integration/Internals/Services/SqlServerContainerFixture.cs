namespace NetEvolve.Pulse.Tests.Integration.Internals;

using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.MsSql;
using TUnit.Core.Interfaces;

public sealed class SqlServerContainerFixture : IAsyncDisposable, IAsyncInitializer
{
    // Stay on the ubuntu-22.04 base: the SQL Server 2025 ubuntu-24.04 images crash intermittently during
    // startup (sqlpal/LSASS fatal error), see https://github.com/microsoft/mssql-docker/issues/974.
    private readonly MsSqlContainer _container = new MsSqlBuilder(
        /*dockerimage*/"mcr.microsoft.com/mssql/server:2025-CU9-ubuntu-22.04"
    )
        .WithLogger(NullLogger.Instance)
        .Build();

    public string ConnectionString => _container.GetConnectionString() + ";MultipleActiveResultSets=True;";

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    public async Task InitializeAsync() => await _container.StartAsync().ConfigureAwait(false);
}
