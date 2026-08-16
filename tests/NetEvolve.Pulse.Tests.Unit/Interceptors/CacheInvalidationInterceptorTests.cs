namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using NetEvolve.Pulse.Internals;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("Interceptors")]
public sealed class CacheInvalidationInterceptorTests
{
    [Test]
    public async Task Constructor_NullServiceProvider_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new CacheInvalidationInterceptor<TestCommand, string>(null!, new InMemoryCacheKeyRegistry()))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_NullCacheKeyRegistry_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new CacheInvalidationInterceptor<TestCommand, string>(
                    new ServiceCollection().BuildServiceProvider(),
                    null!
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task HandleAsync_NonInvalidatingCommand_PassesThroughWithoutCacheInteraction(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var provider = new ServiceCollection().BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var interceptor = new CacheInvalidationInterceptor<NonInvalidatingCommand, string>(
                provider,
                new InMemoryCacheKeyRegistry()
            );
            var command = new NonInvalidatingCommand();
            var handlerCallCount = 0;

            var result = await interceptor
                .HandleAsync(
                    command,
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
    public async Task HandleAsync_SuccessfulInvalidatingCommand_EvictsRegisteredKeysAndClearsRegistry(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var services = new ServiceCollection();
        _ = services.AddDistributedMemoryCache();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var cache = provider.GetRequiredService<IDistributedCache>();
            await cache.SetAsync("query-key", [1, 2, 3], cancellationToken).ConfigureAwait(false);

            var registry = new InMemoryCacheKeyRegistry();
            registry.Register(typeof(TestQuery), "query-key");

            var interceptor = new CacheInvalidationInterceptor<TestCommand, string>(provider, registry);
            var command = new TestCommand([typeof(TestQuery)]);

            var result = await interceptor
                .HandleAsync(command, (_, _) => Task.FromResult("handler-result"), cancellationToken)
                .ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(result).IsEqualTo("handler-result");

                var bytes = await cache.GetAsync("query-key", cancellationToken).ConfigureAwait(false);
                _ = await Assert.That(bytes).IsNull();

                _ = await Assert.That(registry.GetKeysForType(typeof(TestQuery))).IsEmpty();
            }
        }
    }

    [Test]
    public async Task HandleAsync_HandlerThrows_DoesNotEvictCache(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var services = new ServiceCollection();
        _ = services.AddDistributedMemoryCache();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var cache = provider.GetRequiredService<IDistributedCache>();
            await cache.SetAsync("query-key", [1, 2, 3], cancellationToken).ConfigureAwait(false);

            var registry = new InMemoryCacheKeyRegistry();
            registry.Register(typeof(TestQuery), "query-key");

            var interceptor = new CacheInvalidationInterceptor<TestCommand, string>(provider, registry);
            var command = new TestCommand([typeof(TestQuery)]);

            _ = await Assert
                .That(async () =>
                    await interceptor
                        .HandleAsync(
                            command,
                            (_, _) => Task.FromException<string>(new InvalidOperationException("handler error")),
                            cancellationToken
                        )
                        .ConfigureAwait(false)
                )
                .Throws<InvalidOperationException>();

            var bytes = await cache.GetAsync("query-key", cancellationToken).ConfigureAwait(false);
            _ = await Assert.That(bytes).IsNotNull();
        }
    }

    [Test]
    public async Task HandleAsync_MultipleInvalidatedQueryTypes_EvictsKeysForAllTypes(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var services = new ServiceCollection();
        _ = services.AddDistributedMemoryCache();
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var cache = provider.GetRequiredService<IDistributedCache>();
            await cache.SetAsync("query-key-1", [1], cancellationToken).ConfigureAwait(false);
            await cache.SetAsync("query-key-2", [2], cancellationToken).ConfigureAwait(false);

            var registry = new InMemoryCacheKeyRegistry();
            registry.Register(typeof(TestQuery), "query-key-1");
            registry.Register(typeof(AnotherTestQuery), "query-key-2");

            var interceptor = new CacheInvalidationInterceptor<TestCommand, string>(provider, registry);
            var command = new TestCommand([typeof(TestQuery), typeof(AnotherTestQuery)]);

            var result = await interceptor
                .HandleAsync(command, (_, _) => Task.FromResult("handler-result"), cancellationToken)
                .ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(result).IsEqualTo("handler-result");

                var bytes1 = await cache.GetAsync("query-key-1", cancellationToken).ConfigureAwait(false);
                _ = await Assert.That(bytes1).IsNull();

                var bytes2 = await cache.GetAsync("query-key-2", cancellationToken).ConfigureAwait(false);
                _ = await Assert.That(bytes2).IsNull();

                _ = await Assert.That(registry.GetKeysForType(typeof(TestQuery))).IsEmpty();
                _ = await Assert.That(registry.GetKeysForType(typeof(AnotherTestQuery))).IsEmpty();
            }
        }
    }

    [Test]
    public async Task HandleAsync_NoCacheRegistered_CompletesSuccessfullyWithoutEviction(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var provider = new ServiceCollection().BuildServiceProvider();
        // Do NOT register IDistributedCache
        await using (provider.ConfigureAwait(false))
        {
            var registry = new InMemoryCacheKeyRegistry();
            registry.Register(typeof(TestQuery), "query-key");

            var interceptor = new CacheInvalidationInterceptor<TestCommand, string>(provider, registry);
            var command = new TestCommand([typeof(TestQuery)]);
            var handlerCallCount = 0;

            var result = await interceptor
                .HandleAsync(
                    command,
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

    // ── Private test types ───────────────────────────────────────────────────

    private sealed class TestQuery : IQuery<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class AnotherTestQuery : IQuery<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed record NonInvalidatingCommand : ICommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed record TestCommand(IEnumerable<Type> InvalidatedQueryTypes) : IInvalidatingCommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }
}
