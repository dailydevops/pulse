namespace NetEvolve.Pulse.Tests.Integration.Idempotency;

using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Idempotency;
using NetEvolve.Pulse.Idempotency;
using NetEvolve.Pulse.Tests.Integration.Internals;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("Idempotency")]
[Timeout(300_000)] // Increased timeout to accommodate potential delays in CI environments.
public abstract class IdempotencyTestsBase(
    IServiceFixture databaseServiceFixture,
    IServiceInitializer databaseInitializer
)
{
    protected IServiceFixture DatabaseServiceFixture { get; } = databaseServiceFixture;
    protected IServiceInitializer DatabaseInitializer { get; } = databaseInitializer;

    protected static DateTimeOffset TestDateTime { get; } = new DateTimeOffset(2025, 1, 1, 12, 0, 0, 0, TimeSpan.Zero);

    private const int ConcurrentCalls = 20;

    /// <summary>
    /// Gets a value indicating whether the provider reserves keys atomically, so that exactly one of several
    /// concurrent reservations wins and a re-reserved expired key rejects later duplicates.
    /// </summary>
    protected virtual bool SupportsAtomicReservation => false;

    protected async ValueTask RunAndVerify(
        Func<IServiceProvider, CancellationToken, Task> testableCode,
        CancellationToken cancellationToken,
        Action<IServiceCollection>? configureServices = null,
        [CallerMemberName] string tableName = null!
    )
    {
        ArgumentNullException.ThrowIfNull(testableCode);

        cancellationToken.ThrowIfCancellationRequested();

        using var host = new HostBuilder()
            .ConfigureAppConfiguration((hostContext, configBuilder) => { })
            .ConfigureServices(services =>
            {
                DatabaseInitializer.Initialize(services, DatabaseServiceFixture);
                configureServices?.Invoke(services);
                _ = services
                    .AddPulse(mediatorBuilder => DatabaseInitializer.Configure(mediatorBuilder, DatabaseServiceFixture))
                    .Configure<IdempotencyKeyOptions>(options =>
                    {
                        options.TableName = tableName;
                        options.Schema = TestHelper.TargetFramework;
                    });
            })
            .ConfigureWebHost(webBuilder => _ = webBuilder.UseTestServer().Configure(applicationBuilder => { }))
            .Build();

        await DatabaseInitializer.CreateDatabaseAsync(host.Services, cancellationToken).ConfigureAwait(false);
        await host.StartAsync(cancellationToken).ConfigureAwait(false);

        using var server = host.GetTestServer();

        using (Assert.Multiple())
        {
            var scope = server.Services.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                await testableCode.Invoke(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
            }
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
                    // Scope 1 (current scope): store the key
                    var store = services.GetRequiredService<IIdempotencyStore>();
                    await store.StoreAsync("cross-scope-key", token).ConfigureAwait(false);

                    // Scope 2: simulate a concurrent request arriving with the same key.
                    // A fresh scope means a fresh DbContext with an empty change tracker, so
                    // the local-tracker early-exit in StoreAsync will not fire. The insert
                    // reaches the database and triggers a PK/unique-constraint violation,
                    // which must be caught and treated as an idempotent no-op (exercises
                    // the IsDuplicateKeyException code path).
                    var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
                    var scope2 = scopeFactory.CreateAsyncScope();
                    await using (scope2.ConfigureAwait(false))
                    {
                        var store2 = scope2.ServiceProvider.GetRequiredService<IIdempotencyStore>();

                        _ = await Assert
                            .That(async () => await store2.StoreAsync("cross-scope-key", token).ConfigureAwait(false))
                            .ThrowsNothing();

                        var exists = await store2.ExistsAsync("cross-scope-key", token).ConfigureAwait(false);
                        _ = await Assert.That(exists).IsTrue();
                    }
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_Respect_TimeToLive_When_Key_Is_Within_Ttl(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fakeTime = new FakeTimeProvider();
        fakeTime.AdjustTime(TestDateTime);

        await RunAndVerify(
                async (services, token) =>
                {
                    var store = services.GetRequiredService<IIdempotencyStore>();

                    await store.StoreAsync("ttl-key", token).ConfigureAwait(false);

                    // Advance time by 30 minutes — key is still within the 60-minute TTL
                    fakeTime.Advance(TimeSpan.FromMinutes(30));

                    var result = await store.ExistsAsync("ttl-key", token).ConfigureAwait(false);

                    _ = await Assert.That(result).IsTrue();
                },
                cancellationToken,
                configureServices: services =>
                    services
                        .AddSingleton<TimeProvider>(fakeTime)
                        .Configure<IdempotencyKeyOptions>(o => o.TimeToLive = TimeSpan.FromHours(1))
            )
            .ConfigureAwait(false);
    }

    [Test]
    public async Task Should_Treat_Key_As_Absent_When_Ttl_Has_Expired(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fakeTime = new FakeTimeProvider();
        fakeTime.AdjustTime(TestDateTime);

        await RunAndVerify(
                async (services, token) =>
                {
                    var store = services.GetRequiredService<IIdempotencyStore>();

                    await store.StoreAsync("expired-key", token).ConfigureAwait(false);

                    // Advance time beyond the 60-minute TTL
                    fakeTime.Advance(TimeSpan.FromHours(2));

                    var result = await store.ExistsAsync("expired-key", token).ConfigureAwait(false);

                    _ = await Assert.That(result).IsFalse();
                },
                cancellationToken,
                configureServices: services =>
                    services
                        .AddSingleton<TimeProvider>(fakeTime)
                        .Configure<IdempotencyKeyOptions>(o => o.TimeToLive = TimeSpan.FromHours(1))
            )
            .ConfigureAwait(false);
    }

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

    [Test]
    public async Task Should_Reserve_New_Key_Once(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var store = services.GetRequiredService<IIdempotencyStore>();

                    var first = await store.TryReserveAsync("reserve-key", token).ConfigureAwait(false);
                    var second = await store.TryReserveAsync("reserve-key", token).ConfigureAwait(false);

                    using (Assert.Multiple())
                    {
                        _ = await Assert.That(first).IsTrue();
                        _ = await Assert.That(second).IsFalse();
                    }
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_Reserve_Exactly_Once_When_Reserving_Same_Key_Concurrently(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        Skip.When(!SupportsAtomicReservation, "The provider does not reserve idempotency keys atomically.");

        await RunAndVerify(
                async (services, token) =>
                {
                    // Open the provider connection up front, so the parallel calls race on the reservation only.
                    _ = await services
                        .GetRequiredService<IIdempotencyStore>()
                        .ExistsAsync("warm-up", token)
                        .ConfigureAwait(false);

                    var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();

                    var results = await Task.WhenAll(
                            Enumerable
                                .Range(0, ConcurrentCalls)
                                .Select(_ =>
                                    Task.Run(
                                        async () =>
                                        {
                                            var scope = scopeFactory.CreateAsyncScope();
                                            await using (scope.ConfigureAwait(false))
                                            {
                                                var store =
                                                    scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
                                                return await store
                                                    .TryReserveAsync("concurrent-key", token)
                                                    .ConfigureAwait(false);
                                            }
                                        },
                                        token
                                    )
                                )
                        )
                        .ConfigureAwait(false);

                    _ = await Assert.That(results.Count(reserved => reserved)).IsEqualTo(1);
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    [Test]
    public async Task Should_Run_Handler_Once_When_Sending_Same_Command_Concurrently(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        Skip.When(!SupportsAtomicReservation, "The provider does not reserve idempotency keys atomically.");

        var counter = new InvocationCounter();

        await RunAndVerify(
                async (services, token) =>
                {
                    // Open the provider connection up front, so the parallel calls race on the reservation only.
                    _ = await services
                        .GetRequiredService<IIdempotencyStore>()
                        .ExistsAsync("warm-up", token)
                        .ConfigureAwait(false);

                    var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();

                    var outcomes = await Task.WhenAll(
                            Enumerable
                                .Range(0, ConcurrentCalls)
                                .Select(_ =>
                                    Task.Run(
                                        async () =>
                                        {
                                            var scope = scopeFactory.CreateAsyncScope();
                                            await using (scope.ConfigureAwait(false))
                                            {
                                                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                                                try
                                                {
                                                    await mediator
                                                        .SendAsync(new CountingCommand("concurrent-command"), token)
                                                        .ConfigureAwait(false);
                                                    return true;
                                                }
                                                catch (IdempotencyConflictException)
                                                {
                                                    return false;
                                                }
                                            }
                                        },
                                        token
                                    )
                                )
                        )
                        .ConfigureAwait(false);

                    using (Assert.Multiple())
                    {
                        _ = await Assert.That(counter.Count).IsEqualTo(1);
                        _ = await Assert.That(outcomes.Count(succeeded => succeeded)).IsEqualTo(1);
                        _ = await Assert.That(outcomes.Count(succeeded => !succeeded)).IsEqualTo(ConcurrentCalls - 1);
                    }
                },
                cancellationToken,
                configureServices: services =>
                    services
                        .AddSingleton(counter)
                        .AddSingleton<ICommandHandler<CountingCommand, Void>, CountingCommandHandler>()
            )
            .ConfigureAwait(false);
    }

    [Test]
    public async Task Should_Reserve_Logically_Expired_Key_That_Is_Still_Physically_Present(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fakeTime = new FakeTimeProvider();
        fakeTime.AdjustTime(TestDateTime);

        await RunAndVerify(
                async (services, token) =>
                {
                    var store = services.GetRequiredService<IIdempotencyStore>();

                    var first = await store.TryReserveAsync("expired-reserve-key", token).ConfigureAwait(false);

                    // Past the logical TTL, but within the physical expiry (TTL + 1h) of providers that have one.
                    fakeTime.Advance(TimeSpan.FromMinutes(90));

                    var second = await store.TryReserveAsync("expired-reserve-key", token).ConfigureAwait(false);
                    var third = await store.TryReserveAsync("expired-reserve-key", token).ConfigureAwait(false);

                    using (Assert.Multiple())
                    {
                        _ = await Assert.That(first).IsTrue();
                        _ = await Assert.That(second).IsTrue();

                        // Non-atomic providers keep the old timestamp on re-reservation (tracked in #814).
                        if (SupportsAtomicReservation)
                        {
                            _ = await Assert.That(third).IsFalse();
                        }
                    }
                },
                cancellationToken,
                configureServices: services =>
                    services
                        .AddSingleton<TimeProvider>(fakeTime)
                        .Configure<IdempotencyKeyOptions>(o => o.TimeToLive = TimeSpan.FromHours(1))
            )
            .ConfigureAwait(false);
    }

    [Test]
    public async Task Should_Never_Reserve_Existing_Key_Again_When_TimeToLive_Is_Null(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fakeTime = new FakeTimeProvider();
        fakeTime.AdjustTime(TestDateTime);

        await RunAndVerify(
                async (services, token) =>
                {
                    var store = services.GetRequiredService<IIdempotencyStore>();

                    var first = await store.TryReserveAsync("no-ttl-key", token).ConfigureAwait(false);

                    fakeTime.Advance(TimeSpan.FromDays(365));

                    var second = await store.TryReserveAsync("no-ttl-key", token).ConfigureAwait(false);

                    using (Assert.Multiple())
                    {
                        _ = await Assert.That(first).IsTrue();
                        _ = await Assert.That(second).IsFalse();
                    }
                },
                cancellationToken,
                configureServices: services =>
                    services
                        .AddSingleton<TimeProvider>(fakeTime)
                        .Configure<IdempotencyKeyOptions>(o => o.TimeToLive = null)
            )
            .ConfigureAwait(false);
    }

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
        ) => Task.FromResult(Void.Completed);
    }

    private sealed class InvocationCounter
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Increment() => _ = Interlocked.Increment(ref _count);
    }

    private sealed record CountingCommand(string IdempotencyKey) : IIdempotentCommand
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class CountingCommandHandler(InvocationCounter counter) : ICommandHandler<CountingCommand, Void>
    {
        public async Task<Void> HandleAsync(CountingCommand command, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            counter.Increment();

            // Keep the handler busy so overlapping submissions hit the reservation while it runs.
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            return Void.Completed;
        }
    }
}
