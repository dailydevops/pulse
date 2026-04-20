namespace NetEvolve.Pulse.Tests.Integration.Internals;

using TUnit.Core;

/// <summary>
/// Provides a per-test <see cref="IServiceType"/> backed by a Redis Testcontainer.
/// </summary>
public sealed class RedisDatabaseServiceFixture : IServiceType
{
    [ClassDataSource<RedisContainerFixture>(Shared = SharedType.PerTestSession)]
    public RedisContainerFixture Container { get; set; } = default!;

    public string ConnectionString => Container.ConnectionString;

    public ServiceType ServiceType => ServiceType.Redis;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task InitializeAsync() => Task.CompletedTask;
}
