namespace NetEvolve.Pulse.Tests.Integration.AzureQueueStorage;

using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.Azurite;
using TUnit.Core.Interfaces;

/// <summary>
/// Provides a shared Azurite container fixture for integration tests.
/// </summary>
public sealed class AzuriteContainerFixture : IAsyncDisposable, IAsyncInitializer
{
    private readonly AzuriteContainer _container = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.36.0")
        .WithLogger(NullLogger.Instance)
        .Build();

    /// <summary>
    /// Gets the connection string for the running Azurite container.
    /// </summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _container.DisposeAsync();

    /// <inheritdoc />
    public async Task InitializeAsync() => await _container.StartAsync().ConfigureAwait(false);
}
