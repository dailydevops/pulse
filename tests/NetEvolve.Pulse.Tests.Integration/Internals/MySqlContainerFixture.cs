namespace NetEvolve.Pulse.Tests.Integration.Internals;

using Microsoft.Extensions.Logging.Abstractions;
using MySql.Data.MySqlClient;
using Testcontainers.MySql;
using TUnit.Core.Interfaces;

/// <summary>
/// Manages the lifecycle of a MySQL Testcontainer shared across a test session.
/// </summary>
public sealed class MySqlContainerFixture : IAsyncDisposable, IAsyncInitializer
{
    private readonly MySqlContainer _container = new MySqlBuilder(
        /*dockerimage*/"mysql:8.0"
    )
        .WithLogger(NullLogger.Instance)
        .WithUsername(UserName)
        .WithPrivileged(true)
        .Build();

    public string ConnectionString =>
        _container.GetConnectionString() + ";SslMode=Disabled;AllowPublicKeyRetrieval=True;ConnectionTimeout=30;";

    public static string UserName => "root";

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    public async Task InitializeAsync() => await _container.StartAsync().ConfigureAwait(false);
}
