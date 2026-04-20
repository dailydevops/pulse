namespace NetEvolve.Pulse.Tests.Integration.Idempotency;

using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Idempotency;
using NetEvolve.Pulse.Idempotency;
using NetEvolve.Pulse.Tests.Integration.Internals;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// Base class for Redis idempotency integration tests.
/// </summary>
/// <remarks>
/// Redis TTL is managed natively via the key expiry mechanism in the <c>SET NX EX</c> command.
/// Tests that rely on <see cref="Microsoft.Extensions.Time.Testing.FakeTimeProvider"/> to simulate
/// TTL expiry are not applicable here; this class provides a real-TTL test instead.
/// </remarks>
[TestGroup("Idempotency")]
[Timeout(300_000)]
public abstract class RedisIdempotencyTestsBase(
    IServiceFixture databaseServiceFixture,
    IDatabaseInitializer databaseInitializer
)
{
    protected IServiceFixture DatabaseServiceFixture { get; } = databaseServiceFixture;
    protected IDatabaseInitializer DatabaseInitializer { get; } = databaseInitializer;

    protected async ValueTask RunAndVerify(
        Func<IServiceProvider, CancellationToken, Task> testableCode,
        CancellationToken cancellationToken,
        Action<IServiceCollection>? configureServices = null
    )
    {
        ArgumentNullException.ThrowIfNull(testableCode);

        using var host = new HostBuilder()
            .ConfigureAppConfiguration((_, _) => { })
            .ConfigureServices(services =>
            {
                DatabaseInitializer.Initialize(services, DatabaseServiceFixture);
                configureServices?.Invoke(services);
                _ = services.AddPulse(mediatorBuilder =>
                    DatabaseInitializer.Configure(mediatorBuilder, DatabaseServiceFixture)
                );
            })
            .ConfigureWebHost(webBuilder => _ = webBuilder.UseTestServer().Configure(_ => { }))
            .Build();

        await DatabaseInitializer.CreateDatabaseAsync(host.Services, cancellationToken).ConfigureAwait(false);
        await host.StartAsync(cancellationToken).ConfigureAwait(false);

        using var server = host.GetTestServer();

        using (Assert.Multiple())
        {
            await using var scope = server.Services.CreateAsyncScope();
            await testableCode.Invoke(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    [Test]
    public async Task Should_Return_False_When_Key_Does_Not_Exist(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var store = services.GetRequiredService<IIdempotencyStore>();

                    var result = await store.ExistsAsync("non-existent-key", token).ConfigureAwait(false);

                    _ = await Assert.That(result).IsFalse();
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_Return_True_When_Key_Exists(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var store = services.GetRequiredService<IIdempotencyStore>();

                    await store.StoreAsync("my-key", token).ConfigureAwait(false);

                    var result = await store.ExistsAsync("my-key", token).ConfigureAwait(false);

                    _ = await Assert.That(result).IsTrue();
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_Return_False_For_Different_Key(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var store = services.GetRequiredService<IIdempotencyStore>();

                    await store.StoreAsync("key-a", token).ConfigureAwait(false);

                    var result = await store.ExistsAsync("key-b", token).ConfigureAwait(false);

                    _ = await Assert.That(result).IsFalse();
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_Store_Multiple_Keys(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var store = services.GetRequiredService<IIdempotencyStore>();

                    await store.StoreAsync("key-1", token).ConfigureAwait(false);
                    await store.StoreAsync("key-2", token).ConfigureAwait(false);
                    await store.StoreAsync("key-3", token).ConfigureAwait(false);

                    using (Assert.Multiple())
                    {
                        _ = await Assert.That(await store.ExistsAsync("key-1", token).ConfigureAwait(false)).IsTrue();
                        _ = await Assert.That(await store.ExistsAsync("key-2", token).ConfigureAwait(false)).IsTrue();
                        _ = await Assert.That(await store.ExistsAsync("key-3", token).ConfigureAwait(false)).IsTrue();
                    }
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_Be_Idempotent_When_Storing_Duplicate_Key(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var store = services.GetRequiredService<IIdempotencyStore>();

                    await store.StoreAsync("duplicate-key", token).ConfigureAwait(false);

                    // Second store of same key must NOT throw
                    _ = await Assert
                        .That(async () => await store.StoreAsync("duplicate-key", token).ConfigureAwait(false))
                        .ThrowsNothing();

                    // Key should still exist
                    var result = await store.ExistsAsync("duplicate-key", token).ConfigureAwait(false);
                    _ = await Assert.That(result).IsTrue();
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_Handle_Cross_Scope_Duplicate_Insert_Without_Throwing(
        CancellationToken cancellationToken
    ) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var store = services.GetRequiredService<IIdempotencyStore>();
                    await store.StoreAsync("cross-scope-key", token).ConfigureAwait(false);

                    // A second scope means a second Scoped IIdempotencyStore instance.
                    // The SET NX operation is atomic in Redis so this exercises the
                    // "key already existed" code path without throwing.
                    var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
                    await using var scope2 = scopeFactory.CreateAsyncScope();
                    var store2 = scope2.ServiceProvider.GetRequiredService<IIdempotencyStore>();

                    _ = await Assert
                        .That(async () => await store2.StoreAsync("cross-scope-key", token).ConfigureAwait(false))
                        .ThrowsNothing();

                    var exists = await store2.ExistsAsync("cross-scope-key", token).ConfigureAwait(false);
                    _ = await Assert.That(exists).IsTrue();
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    /// <summary>
    /// Verifies that Redis natively expires keys using the TTL set in <see cref="RedisIdempotencyKeyOptions.TimeToLive"/>.
    /// </summary>
    [Test]
    public async Task Should_Expire_Key_When_Redis_Ttl_Elapses(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var store = services.GetRequiredService<IIdempotencyStore>();

                    await store.StoreAsync("ttl-expiry-key", token).ConfigureAwait(false);

                    // Key should exist immediately after storing
                    var existsBeforeExpiry = await store.ExistsAsync("ttl-expiry-key", token).ConfigureAwait(false);
                    _ = await Assert.That(existsBeforeExpiry).IsTrue();

                    // Wait for the 2-second TTL to elapse
                    await Task.Delay(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);

                    // Key should have been evicted by Redis
                    var existsAfterExpiry = await store.ExistsAsync("ttl-expiry-key", token).ConfigureAwait(false);
                    _ = await Assert.That(existsAfterExpiry).IsFalse();
                },
                cancellationToken,
                configureServices: services =>
                    services.Configure<RedisIdempotencyKeyOptions>(opts => opts.TimeToLive = TimeSpan.FromSeconds(2))
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_Enforce_Idempotency_For_Void_Command_Through_Mediator(
        CancellationToken cancellationToken
    ) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    // First execution should succeed
                    var command = new TestIdempotentVoidCommand("idempotent-void-key");
                    await mediator.SendAsync(command, token).ConfigureAwait(false);

                    // Second execution with same key should throw IdempotencyConflictException
                    var duplicateCommand = new TestIdempotentVoidCommand("idempotent-void-key");
                    var exception = await Assert
                        .That(async () => await mediator.SendAsync(duplicateCommand, token).ConfigureAwait(false))
                        .Throws<IdempotencyConflictException>();

                    _ = await Assert.That(exception!.IdempotencyKey).IsEqualTo("idempotent-void-key");
                },
                cancellationToken,
                configureServices: services =>
                    services.AddSingleton<
                        ICommandHandler<TestIdempotentVoidCommand, Void>,
                        TestIdempotentVoidCommandHandler
                    >()
            )
            .ConfigureAwait(false);

    private sealed record TestIdempotentVoidCommand(string IdempotencyKey) : IIdempotentCommand
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class TestIdempotentVoidCommandHandler : ICommandHandler<TestIdempotentVoidCommand, Void>
    {
        public Task<Void> HandleAsync(
            TestIdempotentVoidCommand command,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Extensibility.Void.Completed);
    }
}
