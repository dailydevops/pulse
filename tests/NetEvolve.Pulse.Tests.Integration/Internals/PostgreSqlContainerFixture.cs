namespace NetEvolve.Pulse.Tests.Integration.Internals;

using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

public sealed class PostgreSqlContainerFixture : IAsyncDisposable, IAsyncInitializer
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(
        /*dockerimage*/"postgres:15.17"
    )
        .WithLogger(NullLogger.Instance)
        .Build();

    public string ConnectionString => _container.GetConnectionString() + ";Include Error Detail=true;";

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    public async Task InitializeAsync() => await _container.StartAsync().ConfigureAwait(false);
}
