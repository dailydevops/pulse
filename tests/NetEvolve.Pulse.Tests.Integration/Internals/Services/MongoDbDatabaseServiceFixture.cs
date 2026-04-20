namespace NetEvolve.Pulse.Tests.Integration.Internals.Services;

public sealed class MongoDbDatabaseServiceFixture : IServiceFixture
{
    [ClassDataSource<MongoDbContainerFixture>(Shared = SharedType.PerTestSession)]
    public MongoDbContainerFixture Container { get; set; } = default!;

    public string ConnectionString => Container.ConnectionString;

    public string DatabaseName { get; } = $"pulse{Guid.NewGuid():N}";

    public ServiceType ServiceType => ServiceType.MongoDB;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task InitializeAsync() => Task.CompletedTask;
}
