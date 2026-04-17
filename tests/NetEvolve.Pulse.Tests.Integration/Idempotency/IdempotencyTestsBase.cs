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
    IDatabaseServiceFixture databaseServiceFixture,
    IDatabaseInitializer databaseInitializer
)
{
    protected IDatabaseServiceFixture DatabaseServiceFixture { get; } = databaseServiceFixture;
    protected IDatabaseInitializer DatabaseInitializer { get; } = databaseInitializer;

    protected static DateTimeOffset TestDateTime { get; } = new DateTimeOffset(2025, 1, 1, 12, 0, 0, 0, TimeSpan.Zero);

    protected async ValueTask RunAndVerify(
        Func<IServiceProvider, CancellationToken, Task> testableCode,
        CancellationToken cancellationToken,
        Action<IServiceCollection>? configureServices = null,
        [CallerMemberName] string tableName = null!
    )
    {
        ArgumentNullException.ThrowIfNull(testableCode);

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

    [Test]
    public async Task Should_Respect_TimeToLive_When_Key_Is_Within_Ttl(CancellationToken cancellationToken)
    {
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

    private sealed record TestIdempotentVoidCommand(string IdempotencyKey) : IIdempotentCommand
    {
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
