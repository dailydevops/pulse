namespace NetEvolve.Pulse.Tests.Integration.Internals;

using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.Redis;
using TUnit.Core.Interfaces;

/// <summary>
/// Manages the lifecycle of a Redis Testcontainer shared across a test session.
/// </summary>
public sealed class RedisContainerFixture : IAsyncDisposable, IAsyncInitializer
{
    private readonly RedisContainer _container = new RedisBuilder( /*dockerimage*/
        "redis:7.0.15"
    )
        .WithLogger(NullLogger.Instance)
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    public async Task InitializeAsync() => await _container.StartAsync().ConfigureAwait(false);
}
