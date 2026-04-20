namespace NetEvolve.Pulse.Tests.Integration.Internals;

using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

public sealed class PostgreSqlContainerFixture : IAsyncDisposable, IAsyncInitializer
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(
        /*dockerimage*/"postgres:18.3"
    )
        .WithLogger(NullLogger.Instance)
        .WithCommand("-c", "max_connections=500") // Raised for parallel integration tests; each test creates its own unique database/pool.
        .Build();

    public string ConnectionString => _container.GetConnectionString() + ";Include Error Detail=true;";

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    public async Task InitializeAsync() => await _container.StartAsync().ConfigureAwait(false);
}
