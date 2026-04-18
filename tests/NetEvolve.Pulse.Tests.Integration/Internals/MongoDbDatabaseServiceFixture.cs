namespace NetEvolve.Pulse.Tests.Integration.Internals;

public sealed class MongoDbDatabaseServiceFixture : IDatabaseServiceFixture
{
    [ClassDataSource<MongoDbContainerFixture>(Shared = SharedType.PerTestSession)]
    public MongoDbContainerFixture Container { get; set; } = default!;

    public string ConnectionString => Container.ConnectionString;

    public string DatabaseName { get; } = $"pulse{Guid.NewGuid():N}";

    public DatabaseType DatabaseType => DatabaseType.MongoDB;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task InitializeAsync() => Task.CompletedTask;
}
