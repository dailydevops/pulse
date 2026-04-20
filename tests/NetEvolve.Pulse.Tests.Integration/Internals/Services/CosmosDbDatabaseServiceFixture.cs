namespace NetEvolve.Pulse.Tests.Integration.Internals;

public sealed class CosmosDbDatabaseServiceFixture : IServiceFixture
{
    [ClassDataSource<CosmosDbContainerFixture>(Shared = SharedType.PerTestSession)]
    public CosmosDbContainerFixture Container { get; set; } = default!;

    public string ConnectionString => Container.ConnectionString;

    public System.Net.Http.HttpMessageHandler HttpMessageHandler => Container.HttpMessageHandler;

    public string DatabaseName { get; } = $"pulse{Guid.NewGuid():N}";

    public ServiceType ServiceType => ServiceType.CosmosDb;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task InitializeAsync() => Task.CompletedTask;
}
