namespace NetEvolve.Pulse.Tests.Integration.Internals;

using TUnit.Core;

/// <summary>
/// Provides a per-test <see cref="IDatabaseServiceFixture"/> backed by a Redis Testcontainer.
/// </summary>
public sealed class RedisDatabaseServiceFixture : IDatabaseServiceFixture
{
    [ClassDataSource<RedisContainerFixture>(Shared = SharedType.PerTestSession)]
    public RedisContainerFixture Container { get; set; } = default!;

    public string ConnectionString => Container.ConnectionString;

    public DatabaseType DatabaseType => DatabaseType.Redis;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task InitializeAsync() => Task.CompletedTask;
}
