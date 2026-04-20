namespace NetEvolve.Pulse.Tests.Integration.Internals;

internal sealed class InMemoryDatabaseServiceFixture : IServiceType
{
    public string ConnectionString { get; } = Guid.NewGuid().ToString("N");

    public ServiceType ServiceType => ServiceType.InMemory;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task InitializeAsync() => Task.CompletedTask;
}
