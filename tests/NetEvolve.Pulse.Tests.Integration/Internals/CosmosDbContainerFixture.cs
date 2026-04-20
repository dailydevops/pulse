namespace NetEvolve.Pulse.Tests.Integration.Internals;

using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.CosmosDb;
using TUnit.Core.Interfaces;

public sealed class CosmosDbContainerFixture : IAsyncDisposable, IAsyncInitializer
{
    private readonly CosmosDbContainer _container = new CosmosDbBuilder(
        "mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview"
    )
        .WithLogger(NullLogger.Instance)
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public System.Net.Http.HttpMessageHandler HttpMessageHandler => _container.HttpMessageHandler;

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    public async Task InitializeAsync() => await _container.StartAsync().ConfigureAwait(false);
}
