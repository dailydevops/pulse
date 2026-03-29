namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Idempotency;
using NetEvolve.Pulse.Idempotency;
using NetEvolve.Pulse.Interceptors;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[SuppressMessage(
    "IDisposableAnalyzers.Correctness",
    "CA2000:Dispose objects before losing scope",
    Justification = "ServiceProvider instances are short-lived within test methods"
)]
public sealed class IdempotencyCommandInterceptorTests
{
    [Test]
    public async Task Constructor_NullServiceProvider_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new IdempotencyCommandInterceptor<TestCommand, string>(null!))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_NoStoreRegistered_DoesNotThrow()
    {
        var provider = new ServiceCollection().BuildServiceProvider();

        var interceptor = new IdempotencyCommandInterceptor<TestCommand, string>(provider);

        _ = await Assert.That(interceptor).IsNotNull();
    }

    [Test]
    public async Task HandleAsync_NullHandler_ThrowsArgumentNullException()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        var interceptor = new IdempotencyCommandInterceptor<TestCommand, string>(provider);
        var command = new TestCommand();

        _ = await Assert
            .That(async () => await interceptor.HandleAsync(command, null!).ConfigureAwait(false))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task HandleAsync_NonIdempotentCommand_PassesThroughWithoutStoreInteraction()
    {
        var store = new TrackingIdempotencyStore();
        var services = new ServiceCollection();
        _ = services.AddSingleton<IIdempotencyStore>(store);
        var provider = services.BuildServiceProvider();
        var interceptor = new IdempotencyCommandInterceptor<NonIdempotentCommand, string>(provider);
        var command = new NonIdempotentCommand();
        var handlerCalled = false;

        var result = await interceptor
            .HandleAsync(
                command,
                (_, _) =>
                {
                    handlerCalled = true;
                    return Task.FromResult("response");
                }
            )
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("response");
            _ = await Assert.That(handlerCalled).IsTrue();
            _ = await Assert.That(store.ExistsCallCount).IsEqualTo(0);
            _ = await Assert.That(store.StoreCallCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task HandleAsync_NoStoreRegistered_PassesThroughWithoutError()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        var interceptor = new IdempotencyCommandInterceptor<TestCommand, string>(provider);
        var command = new TestCommand { IdempotencyKey = "key-1" };
        var handlerCalled = false;

        var result = await interceptor
            .HandleAsync(
                command,
                (_, _) =>
                {
                    handlerCalled = true;
                    return Task.FromResult("response");
                }
            )
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("response");
            _ = await Assert.That(handlerCalled).IsTrue();
        }
    }

    [Test]
    public async Task HandleAsync_NewKey_ExecutesHandlerAndStoresKey()
    {
        var store = new TrackingIdempotencyStore();
        var services = new ServiceCollection();
        _ = services.AddSingleton<IIdempotencyStore>(store);
        var provider = services.BuildServiceProvider();
        var interceptor = new IdempotencyCommandInterceptor<TestCommand, string>(provider);
        var command = new TestCommand { IdempotencyKey = "key-new" };
        var handlerCalled = false;

        var result = await interceptor
            .HandleAsync(
                command,
                (_, _) =>
                {
                    handlerCalled = true;
                    return Task.FromResult("response");
                }
            )
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("response");
            _ = await Assert.That(handlerCalled).IsTrue();
            _ = await Assert.That(store.ExistsCallCount).IsEqualTo(1);
            _ = await Assert.That(store.StoreCallCount).IsEqualTo(1);
            _ = await Assert.That(store.StoredKey).IsEqualTo("key-new");
        }
    }

    [Test]
    public async Task HandleAsync_ExistingKey_ThrowsIdempotencyConflictException()
    {
        var store = new TrackingIdempotencyStore(existingKey: "key-dup");
        var services = new ServiceCollection();
        _ = services.AddSingleton<IIdempotencyStore>(store);
        var provider = services.BuildServiceProvider();
        var interceptor = new IdempotencyCommandInterceptor<TestCommand, string>(provider);
        var command = new TestCommand { IdempotencyKey = "key-dup" };
        var handlerCalled = false;

        var exception = await Assert
            .That(async () =>
                await interceptor
                    .HandleAsync(
                        command,
                        (_, _) =>
                        {
                            handlerCalled = true;
                            return Task.FromResult("response");
                        }
                    )
                    .ConfigureAwait(false)
            )
            .Throws<IdempotencyConflictException>();

        using (Assert.Multiple())
        {
            _ = await Assert.That(handlerCalled).IsFalse();
            _ = await Assert.That(exception!.IdempotencyKey).IsEqualTo("key-dup");
            _ = await Assert.That(store.StoreCallCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task HandleAsync_ExistingKey_DoesNotCallHandler()
    {
        var store = new TrackingIdempotencyStore(existingKey: "key-exists");
        var services = new ServiceCollection();
        _ = services.AddSingleton<IIdempotencyStore>(store);
        var provider = services.BuildServiceProvider();
        var interceptor = new IdempotencyCommandInterceptor<TestCommand, string>(provider);
        var command = new TestCommand { IdempotencyKey = "key-exists" };

        _ = await Assert
            .That(async () =>
                await interceptor.HandleAsync(command, (_, _) => Task.FromResult("response")).ConfigureAwait(false)
            )
            .Throws<IdempotencyConflictException>();

        _ = await Assert.That(store.StoreCallCount).IsEqualTo(0);
    }

    [Test]
    public async Task HandleAsync_HandlerThrows_DoesNotStoreKey()
    {
        var store = new TrackingIdempotencyStore();
        var services = new ServiceCollection();
        _ = services.AddSingleton<IIdempotencyStore>(store);
        var provider = services.BuildServiceProvider();
        var interceptor = new IdempotencyCommandInterceptor<TestCommand, string>(provider);
        var command = new TestCommand { IdempotencyKey = "key-throw" };

        _ = await Assert
            .That(async () =>
                await interceptor
                    .HandleAsync(
                        command,
                        (_, _) => Task.FromException<string>(new InvalidOperationException("handler error"))
                    )
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();

        _ = await Assert.That(store.StoreCallCount).IsEqualTo(0);
    }

    #region Test Types

    private sealed record TestCommand : IIdempotentCommand<string>
    {
        public string? CorrelationId { get; set; }
        public string IdempotencyKey { get; init; } = "default-key";
    }

    private sealed record NonIdempotentCommand : ICommand<string>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed class TrackingIdempotencyStore : IIdempotencyStore
    {
        private readonly string? _existingKey;

        public int ExistsCallCount { get; private set; }
        public int StoreCallCount { get; private set; }
        public string? StoredKey { get; private set; }

        public TrackingIdempotencyStore(string? existingKey = null) => _existingKey = existingKey;

        public Task<bool> ExistsAsync(string idempotencyKey, CancellationToken cancellationToken = default)
        {
            ExistsCallCount++;
            return Task.FromResult(idempotencyKey == _existingKey);
        }

        public Task StoreAsync(string idempotencyKey, CancellationToken cancellationToken = default)
        {
            StoreCallCount++;
            StoredKey = idempotencyKey;
            return Task.CompletedTask;
        }
    }

    #endregion
}
