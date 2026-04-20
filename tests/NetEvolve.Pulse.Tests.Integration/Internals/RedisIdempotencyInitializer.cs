namespace NetEvolve.Pulse.Tests.Integration.Internals;

using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using StackExchange.Redis;

/// <summary>
/// Configures the Redis idempotency store for integration tests.
/// </summary>
public sealed class RedisIdempotencyInitializer : IDatabaseInitializer
{
    public void Configure(IMediatorBuilder mediatorBuilder, IDatabaseServiceFixture databaseService)
    {
        ArgumentNullException.ThrowIfNull(mediatorBuilder);
        _ = mediatorBuilder.AddRedisIdempotencyStore();
    }

    public ValueTask CreateDatabaseAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public void Initialize(IServiceCollection services, IDatabaseServiceFixture databaseService)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(databaseService);

        _ = services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(databaseService.ConnectionString)
        );
    }
}
