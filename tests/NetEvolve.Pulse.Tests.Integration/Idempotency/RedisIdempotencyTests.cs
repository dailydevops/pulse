namespace NetEvolve.Pulse.Tests.Integration.Idempotency;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Idempotency;
using NetEvolve.Pulse.Idempotency;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.Idempotency;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[ClassDataSource<RedisServiceFixture, RedisIdempotencyInitializer>(Shared = [SharedType.None, SharedType.None])]
[TestGroup("Redis")]
[InheritsTests]
public class RedisIdempotencyTests(IServiceFixture databaseServiceFixture, IServiceInitializer databaseInitializer)
    : IdempotencyTestsBase(databaseServiceFixture, databaseInitializer)
{
    private const int ConcurrentCalls = 20;

    [Test]
    public async Task Should_Reserve_Exactly_Once_When_Reserving_Same_Key_Concurrently(
        CancellationToken cancellationToken
    ) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    // Connect the shared multiplexer up front, so the parallel calls race on the reservation only.
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

    [Test]
    public async Task Should_Run_Handler_Once_When_Sending_Same_Command_Concurrently(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var counter = new InvocationCounter();

        await RunAndVerify(
                async (services, token) =>
                {
                    // Connect the shared multiplexer up front, so the parallel calls race on the reservation only.
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

                    // The physical Redis expiry is TTL + 1h, so the key is still present after 90 minutes.
                    fakeTime.Advance(TimeSpan.FromMinutes(90));

                    var second = await store.TryReserveAsync("expired-reserve-key", token).ConfigureAwait(false);
                    var third = await store.TryReserveAsync("expired-reserve-key", token).ConfigureAwait(false);

                    using (Assert.Multiple())
                    {
                        _ = await Assert.That(first).IsTrue();
                        _ = await Assert.That(second).IsTrue();
                        _ = await Assert.That(third).IsFalse();
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
