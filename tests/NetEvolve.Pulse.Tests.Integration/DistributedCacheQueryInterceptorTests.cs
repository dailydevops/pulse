namespace NetEvolve.Pulse.Tests.Integration;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using TUnit.Core;

public sealed class DistributedCacheQueryInterceptorTests
{
    [Test]
    public async Task QueryAsync_CacheableQuery_SecondInvocationServedFromCache()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddDistributedMemoryCache();
        _ = services
            .AddPulse(config => config.AddQueryCaching())
            .AddScoped<IQueryHandler<CachedValueQuery, string>, CachedValueQueryHandler>();

        await using var provider = services.BuildServiceProvider();

        await using var scope1 = provider.CreateAsyncScope();
        var mediator1 = scope1.ServiceProvider.GetRequiredService<IMediator>();
        var handler1 =
            scope1.ServiceProvider.GetRequiredService<IQueryHandler<CachedValueQuery, string>>()
            as CachedValueQueryHandler;

        var query = new CachedValueQuery("integration-key");

        var firstResult = await mediator1.QueryAsync<CachedValueQuery, string>(query).ConfigureAwait(false);

        await using var scope2 = provider.CreateAsyncScope();
        var mediator2 = scope2.ServiceProvider.GetRequiredService<IMediator>();
        var handler2 =
            scope2.ServiceProvider.GetRequiredService<IQueryHandler<CachedValueQuery, string>>()
            as CachedValueQueryHandler;

        var secondResult = await mediator2.QueryAsync<CachedValueQuery, string>(query).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(firstResult).IsEqualTo("integration-value");
            _ = await Assert.That(secondResult).IsEqualTo("integration-value");
            _ = await Assert.That(handler1).IsNotNull();
            _ = await Assert.That(handler1!.CallCount).IsEqualTo(1);
            _ = await Assert.That(handler2).IsNotNull();
            _ = await Assert.That(handler2!.CallCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task QueryAsync_NonCacheableQuery_AlwaysReachesHandler()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddDistributedMemoryCache();
        _ = services
            .AddPulse(config => config.AddQueryCaching())
            .AddScoped<IQueryHandler<NonCachedQuery, string>, NonCachedQueryHandler>();

        await using var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var handler =
            scope.ServiceProvider.GetRequiredService<IQueryHandler<NonCachedQuery, string>>() as NonCachedQueryHandler;

        var query = new NonCachedQuery();

        _ = await mediator.QueryAsync<NonCachedQuery, string>(query).ConfigureAwait(false);
        _ = await mediator.QueryAsync<NonCachedQuery, string>(query).ConfigureAwait(false);

        _ = await Assert.That(handler).IsNotNull();
        _ = await Assert.That(handler!.CallCount).IsEqualTo(2);
    }

    [Test]
    public async Task QueryAsync_WithoutDistributedCache_FallsThroughToHandler()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        // Intentionally no IDistributedCache registration
        _ = services
            .AddPulse(config => config.AddQueryCaching())
            .AddScoped<IQueryHandler<CachedValueQuery, string>, CachedValueQueryHandler>();

        await using var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var handler =
            scope.ServiceProvider.GetRequiredService<IQueryHandler<CachedValueQuery, string>>()
            as CachedValueQueryHandler;

        var query = new CachedValueQuery("no-cache-key");

        var result = await mediator.QueryAsync<CachedValueQuery, string>(query).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("integration-value");
            _ = await Assert.That(handler).IsNotNull();
            _ = await Assert.That(handler!.CallCount).IsEqualTo(1);
        }
    }

    // ── Private test types ───────────────────────────────────────────────────

    private sealed record CachedValueQuery(string Key) : ICacheableQuery<string>
    {
        public string? CorrelationId { get; set; }
        public string CacheKey => Key;
        public TimeSpan? Expiry => null;
    }

    private sealed class NonCachedQuery : IQuery<string>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed class CachedValueQueryHandler : IQueryHandler<CachedValueQuery, string>
    {
        public int CallCount { get; private set; }

        public Task<string> HandleAsync(CachedValueQuery query, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult("integration-value");
        }
    }

    private sealed class NonCachedQueryHandler : IQueryHandler<NonCachedQuery, string>
    {
        public int CallCount { get; private set; }

        public Task<string> HandleAsync(NonCachedQuery query, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult("non-cached-value");
        }
    }
}
