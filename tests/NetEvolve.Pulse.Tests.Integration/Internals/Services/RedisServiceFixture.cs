namespace NetEvolve.Pulse.Tests.Integration.Internals;

using TUnit.Core;

/// <summary>
/// Provides a per-test <see cref="IServiceFixture"/> backed by a Redis Testcontainer.
/// </summary>
public sealed class RedisServiceFixture : IServiceFixture
{
    [ClassDataSource<RedisContainerFixture>(Shared = SharedType.PerTestSession)]
    public RedisContainerFixture Container { get; set; } = default!;

    public string ConnectionString => Container.ConnectionString;

    public ServiceType ServiceType => ServiceType.Redis;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task InitializeAsync() => Task.CompletedTask;
}
