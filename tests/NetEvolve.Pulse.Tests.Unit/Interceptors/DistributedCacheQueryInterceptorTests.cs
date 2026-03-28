namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using TUnit.Core;

public class DistributedCacheQueryInterceptorTests
{
    private static IOptions<QueryCachingOptions> DefaultOptions => Options.Create(new QueryCachingOptions());

    [Test]
    public async Task HandleAsync_QueryNotCacheable_AlwaysCallsHandler()
    {
        var services = new ServiceCollection();
        _ = services.AddDistributedMemoryCache();
        await using var provider = services.BuildServiceProvider();

        var interceptor = new DistributedCacheQueryInterceptor<NonCacheableQuery, string>(provider, DefaultOptions);
        var query = new NonCacheableQuery();
        var handlerCallCount = 0;

        var result = await interceptor
            .HandleAsync(
                query,
                (_, _) =>
                {
                    handlerCallCount++;
                    return Task.FromResult("handler-result");
                }
            )
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("handler-result");
            _ = await Assert.That(handlerCallCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task HandleAsync_CacheMiss_CallsHandlerAndStoresResult()
    {
        var services = new ServiceCollection();
        _ = services.AddDistributedMemoryCache();
        await using var provider = services.BuildServiceProvider();

        var interceptor = new DistributedCacheQueryInterceptor<CacheableQuery, string>(provider, DefaultOptions);
        var query = new CacheableQuery("test-key");
        var handlerCallCount = 0;

        var result = await interceptor
            .HandleAsync(
                query,
                (_, _) =>
                {
                    handlerCallCount++;
                    return Task.FromResult("cached-value");
                }
            )
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("cached-value");
            _ = await Assert.That(handlerCallCount).IsEqualTo(1);
        }

        // Verify the value was written to the cache
        var cache = provider.GetRequiredService<IDistributedCache>();
        var bytes = await cache.GetAsync("test-key").ConfigureAwait(false);
        _ = await Assert.That(bytes).IsNotNull();
        var deserialised = JsonSerializer.Deserialize<string>(bytes!);
        _ = await Assert.That(deserialised).IsEqualTo("cached-value");
    }

    [Test]
    public async Task HandleAsync_CacheHit_ReturnsCachedValueWithoutCallingHandler()
    {
        var services = new ServiceCollection();
        _ = services.AddDistributedMemoryCache();
        await using var provider = services.BuildServiceProvider();

        // Pre-populate the cache
        var cache = provider.GetRequiredService<IDistributedCache>();
        var serialized = JsonSerializer.SerializeToUtf8Bytes("cached-result");
        await cache.SetAsync("hit-key", serialized).ConfigureAwait(false);

        var interceptor = new DistributedCacheQueryInterceptor<CacheableQuery, string>(provider, DefaultOptions);
        var query = new CacheableQuery("hit-key");
        var handlerCallCount = 0;

        var result = await interceptor
            .HandleAsync(
                query,
                (_, _) =>
                {
                    handlerCallCount++;
                    return Task.FromResult("handler-result");
                }
            )
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("cached-result");
            _ = await Assert.That(handlerCallCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task HandleAsync_WithExpiry_StoresEntryWithAbsoluteExpiration()
    {
        var services = new ServiceCollection();
        _ = services.AddDistributedMemoryCache();
        await using var provider = services.BuildServiceProvider();

        var interceptor = new DistributedCacheQueryInterceptor<CacheableQueryWithExpiry, string>(
            provider,
            DefaultOptions
        );
        var query = new CacheableQueryWithExpiry("expiry-key", TimeSpan.FromSeconds(60));

        var result = await interceptor
            .HandleAsync(query, (_, _) => Task.FromResult("expiry-value"))
            .ConfigureAwait(false);

        _ = await Assert.That(result).IsEqualTo("expiry-value");

        var cache = provider.GetRequiredService<IDistributedCache>();
        var bytes = await cache.GetAsync("expiry-key").ConfigureAwait(false);
        _ = await Assert.That(bytes).IsNotNull();
    }

    [Test]
    public async Task HandleAsync_WithNullExpiry_StoresEntryWithoutExpiration()
    {
        var services = new ServiceCollection();
        _ = services.AddDistributedMemoryCache();
        await using var provider = services.BuildServiceProvider();

        var interceptor = new DistributedCacheQueryInterceptor<CacheableQuery, string>(provider, DefaultOptions);
        var query = new CacheableQuery("no-expiry-key");

        var result = await interceptor
            .HandleAsync(query, (_, _) => Task.FromResult("no-expiry-value"))
            .ConfigureAwait(false);

        _ = await Assert.That(result).IsEqualTo("no-expiry-value");

        var cache = provider.GetRequiredService<IDistributedCache>();
        var bytes = await cache.GetAsync("no-expiry-key").ConfigureAwait(false);
        _ = await Assert.That(bytes).IsNotNull();
    }

    [Test]
    public async Task HandleAsync_NoCacheRegistered_FallsThroughToHandler()
    {
        var services = new ServiceCollection();
        // Do NOT register IDistributedCache
        await using var provider = services.BuildServiceProvider();

        var interceptor = new DistributedCacheQueryInterceptor<CacheableQuery, string>(provider, DefaultOptions);
        var query = new CacheableQuery("some-key");
        var handlerCallCount = 0;

        var result = await interceptor
            .HandleAsync(
                query,
                (_, _) =>
                {
                    handlerCallCount++;
                    return Task.FromResult("fallthrough-result");
                }
            )
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("fallthrough-result");
            _ = await Assert.That(handlerCallCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task HandleAsync_ExpiredCacheEntry_CallsHandlerAndRefreshesCache()
    {
        var services = new ServiceCollection();
        _ = services.AddDistributedMemoryCache();
        await using var provider = services.BuildServiceProvider();

        var cache = provider.GetRequiredService<IDistributedCache>();
        var serialized = JsonSerializer.SerializeToUtf8Bytes("stale-value");
        // Store with an already-expired entry (1 ms TTL)
        await cache
            .SetAsync(
                "expired-key",
                serialized,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(1) }
            )
            .ConfigureAwait(false);

        await Task.Delay(50).ConfigureAwait(false); // Allow the entry to expire

        var interceptor = new DistributedCacheQueryInterceptor<CacheableQuery, string>(provider, DefaultOptions);
        var query = new CacheableQuery("expired-key");
        var handlerCallCount = 0;

        var result = await interceptor
            .HandleAsync(
                query,
                (_, _) =>
                {
                    handlerCallCount++;
                    return Task.FromResult("fresh-value");
                }
            )
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("fresh-value");
            _ = await Assert.That(handlerCallCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task HandleAsync_SlidingExpirationMode_StoresEntryWithSlidingExpiration()
    {
        var services = new ServiceCollection();
        _ = services.AddDistributedMemoryCache();
        await using var provider = services.BuildServiceProvider();

        var options = Options.Create(new QueryCachingOptions { ExpirationMode = CacheExpirationMode.Sliding });
        var interceptor = new DistributedCacheQueryInterceptor<CacheableQueryWithExpiry, string>(provider, options);
        var query = new CacheableQueryWithExpiry("sliding-key", TimeSpan.FromSeconds(60));

        var result = await interceptor
            .HandleAsync(query, (_, _) => Task.FromResult("sliding-value"))
            .ConfigureAwait(false);

        _ = await Assert.That(result).IsEqualTo("sliding-value");

        // Entry should still be accessible after being stored with sliding expiration
        var cache = provider.GetRequiredService<IDistributedCache>();
        var bytes = await cache.GetAsync("sliding-key").ConfigureAwait(false);
        _ = await Assert.That(bytes).IsNotNull();
    }

    [Test]
    public async Task HandleAsync_CustomJsonSerializerOptions_UsedForSerializationAndDeserialization()
    {
        var services = new ServiceCollection();
        _ = services.AddDistributedMemoryCache();
        await using var provider = services.BuildServiceProvider();

        var customJsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var options = Options.Create(new QueryCachingOptions { JsonSerializerOptions = customJsonOptions });

        var interceptor = new DistributedCacheQueryInterceptor<CacheableQuery, string>(provider, options);
        var query = new CacheableQuery("custom-json-key");

        var result = await interceptor
            .HandleAsync(query, (_, _) => Task.FromResult("custom-json-value"))
            .ConfigureAwait(false);

        _ = await Assert.That(result).IsEqualTo("custom-json-value");

        // Second call should return from cache using the same custom options
        var cachedResult = await interceptor
            .HandleAsync(query, (_, _) => Task.FromResult("should-not-be-returned"))
            .ConfigureAwait(false);

        _ = await Assert.That(cachedResult).IsEqualTo("custom-json-value");
    }

    [Test]
    public async Task HandleAsync_DefaultExpiry_UsedWhenQueryExpiryIsNull()
    {
        var services = new ServiceCollection();
        _ = services.AddDistributedMemoryCache();
        await using var provider = services.BuildServiceProvider();

        var options = Options.Create(new QueryCachingOptions { DefaultExpiry = TimeSpan.FromMinutes(5) });
        var interceptor = new DistributedCacheQueryInterceptor<CacheableQuery, string>(provider, options);
        var query = new CacheableQuery("default-expiry-key");

        var result = await interceptor
            .HandleAsync(query, (_, _) => Task.FromResult("default-expiry-value"))
            .ConfigureAwait(false);

        _ = await Assert.That(result).IsEqualTo("default-expiry-value");

        // Entry should be present (default expiry applied)
        var cache = provider.GetRequiredService<IDistributedCache>();
        var bytes = await cache.GetAsync("default-expiry-key").ConfigureAwait(false);
        _ = await Assert.That(bytes).IsNotNull();
    }

    [Test]
    public async Task HandleAsync_DefaultExpiry_NotUsedWhenQueryExpiryIsProvided()
    {
        var services = new ServiceCollection();
        _ = services.AddDistributedMemoryCache();
        await using var provider = services.BuildServiceProvider();

        // DefaultExpiry set but query overrides with its own expiry value
        var options = Options.Create(new QueryCachingOptions { DefaultExpiry = TimeSpan.FromMilliseconds(1) });
        var interceptor = new DistributedCacheQueryInterceptor<CacheableQueryWithExpiry, string>(provider, options);
        var query = new CacheableQueryWithExpiry("query-expiry-key", TimeSpan.FromMinutes(10));

        var result = await interceptor
            .HandleAsync(query, (_, _) => Task.FromResult("query-expiry-value"))
            .ConfigureAwait(false);

        _ = await Assert.That(result).IsEqualTo("query-expiry-value");

        // Entry should still be present because the query's own expiry (10 min) overrode DefaultExpiry (1 ms)
        var cache = provider.GetRequiredService<IDistributedCache>();
        var bytes = await cache.GetAsync("query-expiry-key").ConfigureAwait(false);
        _ = await Assert.That(bytes).IsNotNull();
    }

    // ── Private test types ───────────────────────────────────────────────────

    private sealed class NonCacheableQuery : IQuery<string>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record CacheableQuery(string Key) : ICacheableQuery<string>
    {
        public string? CorrelationId { get; set; }
        public string CacheKey => Key;
        public TimeSpan? Expiry => null;
    }

    private sealed record CacheableQueryWithExpiry(string Key, TimeSpan ExpiryValue) : ICacheableQuery<string>
    {
        public string? CorrelationId { get; set; }
        public string CacheKey => Key;
        public TimeSpan? Expiry => ExpiryValue;
    }
}
