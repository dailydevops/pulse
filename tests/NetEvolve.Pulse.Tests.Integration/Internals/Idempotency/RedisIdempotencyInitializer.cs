namespace NetEvolve.Pulse.Tests.Integration.Internals.Idempotency;

using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using StackExchange.Redis;

/// <summary>
/// Configures the Redis idempotency store for integration tests.
/// </summary>
public sealed class RedisIdempotencyInitializer : IServiceInitializer
{
    public void Configure(IMediatorBuilder mediatorBuilder, IServiceFixture serviceFixture)
    {
        ArgumentNullException.ThrowIfNull(mediatorBuilder);
        _ = mediatorBuilder.AddRedisIdempotencyStore();
    }

    public ValueTask CreateDatabaseAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public void Initialize(IServiceCollection services, IServiceFixture serviceFixture)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(serviceFixture);

        _ = services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(serviceFixture.ConnectionString)
        );
    }
}
