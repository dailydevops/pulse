namespace NetEvolve.Pulse.Tests.Unit.CosmosDb;

using Microsoft.Azure.Cosmos;

/// <summary>
/// Minimal test double for <see cref="CosmosClient"/> that returns a preconfigured container.
/// </summary>
internal sealed class FakeCosmosClient : CosmosClient
{
    private readonly Container _container;

    public FakeCosmosClient(Container container) => _container = container;

    public override Container GetContainer(string databaseId, string containerId) => _container;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2215:Dispose methods should call base class dispose",
        Justification = "The mocking constructor does not initialize the disposable client state."
    )]
    protected override void Dispose(bool disposing)
    {
        // Intentionally empty: the mock constructor does not initialize the disposable client state.
    }
}
