namespace NetEvolve.Pulse.Tests.Integration.Internals.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.MongoDb;
using TUnit.Core.Interfaces;

public sealed class MongoDbContainerFixture : IAsyncDisposable, IAsyncInitializer
{
    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:8.0")
        .WithLogger(NullLogger.Instance)
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    public async Task InitializeAsync() => await _container.StartAsync().ConfigureAwait(false);
}
