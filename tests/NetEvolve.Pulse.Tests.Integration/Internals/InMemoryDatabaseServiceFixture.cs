namespace NetEvolve.Pulse.Tests.Integration.Internals;

internal sealed class InMemoryDatabaseServiceFixture : IDatabaseServiceFixture
{
    internal sealed class InMemoryDatabaseServiceFixture : IDatabaseServiceFixture
    {
        private readonly string _connectionString = Guid.NewGuid().ToString("N");
        public string ConnectionString => _connectionString;
    }

    public DatabaseType DatabaseType => DatabaseType.InMemory;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task InitializeAsync() => Task.CompletedTask;
}
