namespace NetEvolve.Pulse.Tests.Integration.Internals;

internal sealed class InMemoryDatabaseServiceFixture : IDatabaseServiceFixture
{
    public string ConnectionString => Guid.NewGuid().ToString("N");

    public DatabaseType DatabaseType => DatabaseType.InMemory;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task InitializeAsync() => Task.CompletedTask;
}
