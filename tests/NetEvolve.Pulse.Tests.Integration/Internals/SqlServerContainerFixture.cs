namespace NetEvolve.Pulse.Tests.Integration.Internals;

using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.MsSql;
using TUnit.Core.Interfaces;

public sealed class SqlServerContainerFixture : IAsyncDisposable, IAsyncInitializer
{
    private readonly MsSqlContainer _container = new MsSqlBuilder(
        /*dockerimage*/"mcr.microsoft.com/mssql/server:2022-RTM-ubuntu-20.04"
    )
        .WithLogger(NullLogger.Instance)
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    public async Task InitializeAsync() => await _container.StartAsync().ConfigureAwait(false);
}
