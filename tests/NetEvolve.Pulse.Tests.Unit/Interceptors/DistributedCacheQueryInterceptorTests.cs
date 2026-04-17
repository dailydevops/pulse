namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Caching;
using NetEvolve.Pulse.Interceptors;
using NetEvolve.Pulse.Serialization;
using TUnit.Core;

[TestGroup("Interceptors")]
public class DistributedCacheQueryInterceptorTests
{
    private static IOptions<QueryCachingOptions> DefaultOptions => Options.Create(new QueryCachingOptions());

#pragma warning disable CA1859 // property intentionally typed as IPayloadSerializer for test flexibility
    private static IPayloadSerializer DefaultSerializer =>
        new SystemTextJsonPayloadSerializer(Options.Create(JsonSerializerOptions.Default));
#pragma warning restore CA1859

    [Test]
    public async Task Constructor_When_serviceProvider_is_null_throws_ArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new DistributedCacheQueryInterceptor<NonCacheableQuery, string>(
                    null!,
                    DefaultOptions,
                    DefaultSerializer
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_When_options_is_null_throws_ArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new DistributedCacheQueryInterceptor<NonCacheableQuery, string>(
                    new ServiceCollection().BuildServiceProvider(),
                    null!,
                    DefaultSerializer
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_When_payloadSerializer_is_null_throws_ArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new DistributedCacheQueryInterceptor<NonCacheableQuery, string>(
                    new ServiceCollection().BuildServiceProvider(),
                    DefaultOptions,
                    null!
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task HandleAsync_QueryNotCacheable_AlwaysCallsHandler(CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        _ = services.AddDistributedMemoryCache();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var interceptor = new DistributedCacheQueryInterceptor<NonCacheableQuery, string>(
                provider,
                DefaultOptions,
                DefaultSerializer
            );
            var query = new NonCacheableQuery();
            var handlerCallCount = 0;

            var result = await interceptor
                .HandleAsync(
                    query,
                    (_, _) =>
                    {
                        handlerCallCount++;
                        return Task.FromResult("handler-result");
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(result).IsEqualTo("handler-result");
                _ = await Assert.That(handlerCallCount).IsEqualTo(1);
            }
        }
    }

    [Test]
    public async Task HandleAsync_CacheMiss_CallsHandlerAndStoresResult(CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        _ = services.AddDistributedMemoryCache();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var interceptor = new DistributedCacheQueryInterceptor<CacheableQuery, string>(
                provider,
                DefaultOptions,
                DefaultSerializer
            );
            var query = new CacheableQuery("test-key");
            var handlerCallCount = 0;

            var result = await interceptor
                .HandleAsync(
                    query,
                    (_, _) =>
                    {
                        handlerCallCount++;
                        return Task.FromResult("cached-value");
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(result).IsEqualTo("cached-value");
                _ = await Assert.That(handlerCallCount).IsEqualTo(1);
            }

            // Verify the value was written to the cache
            var cache = provider.GetRequiredService<IDistributedCache>();
            var bytes = await cache.GetAsync("test-key", cancellationToken).ConfigureAwait(false);
            _ = await Assert.That(bytes).IsNotNull();
            var deserialised = DefaultSerializer.Deserialize<string>(bytes!);
            _ = await Assert.That(deserialised).IsEqualTo("cached-value");
        }
    }

    [Test]
    public async Task HandleAsync_CacheHit_ReturnsCachedValueWithoutCallingHandler(CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        _ = services.AddDistributedMemoryCache();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            // Pre-populate the cache
            var cache = provider.GetRequiredService<IDistributedCache>();
            var serialized = DefaultSerializer.SerializeToBytes("cached-result");
            await cache.SetAsync("hit-key", serialized, cancellationToken).ConfigureAwait(false);

            var interceptor = new DistributedCacheQueryInterceptor<CacheableQuery, string>(
                provider,
                DefaultOptions,
                DefaultSerializer
            );
            var query = new CacheableQuery("hit-key");
            var handlerCallCount = 0;

            var result = await interceptor
                .HandleAsync(
                    query,
                    (_, _) =>
                    {
                        handlerCallCount++;
                        return Task.FromResult("handler-result");
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(result).IsEqualTo("cached-result");
                _ = await Assert.That(handlerCallCount).IsEqualTo(0);
            }
        }
    }

    [Test]
    public async Task HandleAsync_WithExpiry_StoresEntryWithAbsoluteExpiration(CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        _ = services.AddDistributedMemoryCache();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var interceptor = new DistributedCacheQueryInterceptor<CacheableQueryWithExpiry, string>(
                provider,
                DefaultOptions,
                DefaultSerializer
            );
            var query = new CacheableQueryWithExpiry("expiry-key", TimeSpan.FromSeconds(60));

            var result = await interceptor
                .HandleAsync(query, (_, _) => Task.FromResult("expiry-value"), cancellationToken)
                .ConfigureAwait(false);

            _ = await Assert.That(result).IsEqualTo("expiry-value");

            var cache = provider.GetRequiredService<IDistributedCache>();
            var bytes = await cache.GetAsync("expiry-key", cancellationToken).ConfigureAwait(false);
            _ = await Assert.That(bytes).IsNotNull();
        }
    }

    [Test]
    public async Task HandleAsync_WithNullExpiry_StoresEntryWithoutExpiration(CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        _ = services.AddDistributedMemoryCache();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var interceptor = new DistributedCacheQueryInterceptor<CacheableQuery, string>(
                provider,
                DefaultOptions,
                DefaultSerializer
            );
            var query = new CacheableQuery("no-expiry-key");

            var result = await interceptor
                .HandleAsync(query, (_, _) => Task.FromResult("no-expiry-value"), cancellationToken)
                .ConfigureAwait(false);

            _ = await Assert.That(result).IsEqualTo("no-expiry-value");

            var cache = provider.GetRequiredService<IDistributedCache>();
            var bytes = await cache.GetAsync("no-expiry-key", cancellationToken).ConfigureAwait(false);
            _ = await Assert.That(bytes).IsNotNull();
        }
    }

    [Test]
    public async Task HandleAsync_NoCacheRegistered_FallsThroughToHandler(CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        // Do NOT register IDistributedCache
        var provider = services.BuildServiceProvider();
        // Do NOT register IDistributedCache
        await using (provider.ConfigureAwait(false))
        {
            var interceptor = new DistributedCacheQueryInterceptor<CacheableQuery, string>(
                provider,
                DefaultOptions,
                DefaultSerializer
            );
            var query = new CacheableQuery("some-key");
            var handlerCallCount = 0;

            var result = await interceptor
                .HandleAsync(
                    query,
                    (_, _) =>
                    {
                        handlerCallCount++;
                        return Task.FromResult("fallthrough-result");
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(result).IsEqualTo("fallthrough-result");
                _ = await Assert.That(handlerCallCount).IsEqualTo(1);
            }
        }
    }

    [Test]
    public async Task HandleAsync_ExpiredCacheEntry_CallsHandlerAndRefreshesCache(CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        _ = services.AddDistributedMemoryCache();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var cache = provider.GetRequiredService<IDistributedCache>();
            var serialized = DefaultSerializer.SerializeToBytes("stale-value");
            // Store with an already-expired entry (1 ms TTL)
            await cache
                .SetAsync(
                    "expired-key",
                    serialized,
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(1) },
                    cancellationToken
                )
                .ConfigureAwait(false);

            await Task.Delay(50, cancellationToken).ConfigureAwait(false); // Allow the entry to expire

            var interceptor = new DistributedCacheQueryInterceptor<CacheableQuery, string>(
                provider,
                DefaultOptions,
                DefaultSerializer
            );
            var query = new CacheableQuery("expired-key");
            var handlerCallCount = 0;

            var result = await interceptor
                .HandleAsync(
                    query,
                    (_, _) =>
                    {
                        handlerCallCount++;
                        return Task.FromResult("fresh-value");
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(result).IsEqualTo("fresh-value");
                _ = await Assert.That(handlerCallCount).IsEqualTo(1);
            }
        }
    }

    [Test]
    public async Task HandleAsync_SlidingExpirationMode_StoresEntryWithSlidingExpiration(
        CancellationToken cancellationToken
    )
    {
        var services = new ServiceCollection();
        _ = services.AddDistributedMemoryCache();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = Options.Create(new QueryCachingOptions { ExpirationMode = CacheExpirationMode.Sliding });
            var interceptor = new DistributedCacheQueryInterceptor<CacheableQueryWithExpiry, string>(
                provider,
                options,
                DefaultSerializer
            );
            var query = new CacheableQueryWithExpiry("sliding-key", TimeSpan.FromSeconds(60));

            var result = await interceptor
                .HandleAsync(query, (_, _) => Task.FromResult("sliding-value"), cancellationToken)
                .ConfigureAwait(false);

            _ = await Assert.That(result).IsEqualTo("sliding-value");

            // Entry should still be accessible after being stored with sliding expiration
            var cache = provider.GetRequiredService<IDistributedCache>();
            var bytes = await cache.GetAsync("sliding-key", cancellationToken).ConfigureAwait(false);
            _ = await Assert.That(bytes).IsNotNull();
        }
    }

    [Test]
    public async Task HandleAsync_SecondCall_ReturnsCachedValueWithoutCallingHandler(
        CancellationToken cancellationToken
    )
    {
        var services = new ServiceCollection();
        _ = services.AddDistributedMemoryCache();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var interceptor = new DistributedCacheQueryInterceptor<CacheableQuery, string>(
                provider,
                DefaultOptions,
                DefaultSerializer
            );
            var query = new CacheableQuery("second-call-key");

            var result = await interceptor
                .HandleAsync(query, (_, _) => Task.FromResult("first-value"), cancellationToken)
                .ConfigureAwait(false);

            _ = await Assert.That(result).IsEqualTo("first-value");

            // Second call should return from cache without invoking the handler
            var cachedResult = await interceptor
                .HandleAsync(query, (_, _) => Task.FromResult("should-not-be-returned"), cancellationToken)
                .ConfigureAwait(false);

            _ = await Assert.That(cachedResult).IsEqualTo("first-value");
        }
    }

    [Test]
    public async Task HandleAsync_DefaultExpiry_UsedWhenQueryExpiryIsNull(CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        _ = services.AddDistributedMemoryCache();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = Options.Create(new QueryCachingOptions { DefaultExpiry = TimeSpan.FromMinutes(5) });
            var interceptor = new DistributedCacheQueryInterceptor<CacheableQuery, string>(
                provider,
                options,
                DefaultSerializer
            );
            var query = new CacheableQuery("default-expiry-key");

            var result = await interceptor
                .HandleAsync(query, (_, _) => Task.FromResult("default-expiry-value"), cancellationToken)
                .ConfigureAwait(false);

            _ = await Assert.That(result).IsEqualTo("default-expiry-value");

            // Entry should be present (default expiry applied)
            var cache = provider.GetRequiredService<IDistributedCache>();
            var bytes = await cache.GetAsync("default-expiry-key", cancellationToken).ConfigureAwait(false);
            _ = await Assert.That(bytes).IsNotNull();
        }
    }

    [Test]
    public async Task HandleAsync_DefaultExpiry_NotUsedWhenQueryExpiryIsProvided(CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        _ = services.AddDistributedMemoryCache();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            // DefaultExpiry set but query overrides with its own expiry value
            var options = Options.Create(new QueryCachingOptions { DefaultExpiry = TimeSpan.FromMilliseconds(1) });
            var interceptor = new DistributedCacheQueryInterceptor<CacheableQueryWithExpiry, string>(
                provider,
                options,
                DefaultSerializer
            );
            var query = new CacheableQueryWithExpiry("query-expiry-key", TimeSpan.FromMinutes(10));

            var result = await interceptor
                .HandleAsync(query, (_, _) => Task.FromResult("query-expiry-value"), cancellationToken)
                .ConfigureAwait(false);

            _ = await Assert.That(result).IsEqualTo("query-expiry-value");

            // Entry should still be present because the query's own expiry (10 min) overrode DefaultExpiry (1 ms)
            var cache = provider.GetRequiredService<IDistributedCache>();
            var bytes = await cache.GetAsync("query-expiry-key", cancellationToken).ConfigureAwait(false);
            _ = await Assert.That(bytes).IsNotNull();
        }
    }

    [Test]
    public async Task HandleAsync_NullResponse_SkipsCaching(CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        _ = services.AddDistributedMemoryCache();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var interceptor = new DistributedCacheQueryInterceptor<CacheableQuery, string>(
                provider,
                DefaultOptions,
                DefaultSerializer
            );
            var query = new CacheableQuery("null-response-key");

            var result = await interceptor
                .HandleAsync(query, (_, _) => Task.FromResult<string>(null!), cancellationToken)
                .ConfigureAwait(false);

            _ = await Assert.That(result).IsNull();

            // Nothing should have been written to the cache
            var cache = provider.GetRequiredService<IDistributedCache>();
            var bytes = await cache.GetAsync("null-response-key", cancellationToken).ConfigureAwait(false);
            _ = await Assert.That(bytes).IsNull();
        }
    }

    [Test]
    public async Task HandleAsync_NullDeserializedFromCache_FallsThroughToHandler(CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        _ = services.AddDistributedMemoryCache();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            // Pre-populate the cache with bytes that the serializer will deserialize as null
            var cache = provider.GetRequiredService<IDistributedCache>();
            var nullBytes = DefaultSerializer.SerializeToBytes<string>(null!);
            await cache.SetAsync("null-cached-key", nullBytes, cancellationToken).ConfigureAwait(false);

            var interceptor = new DistributedCacheQueryInterceptor<CacheableQuery, string>(
                provider,
                DefaultOptions,
                DefaultSerializer
            );
            var query = new CacheableQuery("null-cached-key");
            var handlerCallCount = 0;

            var result = await interceptor
                .HandleAsync(
                    query,
                    (_, _) =>
                    {
                        handlerCallCount++;
                        return Task.FromResult("fallback-value");
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(result).IsEqualTo("fallback-value");
                _ = await Assert.That(handlerCallCount).IsEqualTo(1);

                // Stale null bytes must have been evicted and replaced with the fresh handler value
                var afterBytes = await cache.GetAsync("null-cached-key", cancellationToken).ConfigureAwait(false);
                _ = await Assert.That(afterBytes).IsNotNull();
                var afterValue = DefaultSerializer.Deserialize<string>(afterBytes!);
                _ = await Assert.That(afterValue).IsEqualTo("fallback-value");
            }
        }
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
