namespace NetEvolve.Pulse.Tests.Integration.Idempotency;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Idempotency;
using NetEvolve.Pulse.Idempotency;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.Idempotency;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;
using StackExchange.Redis;

[ClassDataSource<RedisServiceFixture, RedisIdempotencyInitializer>(Shared = [SharedType.None, SharedType.None])]
[TestGroup("Redis")]
[InheritsTests]
public class RedisIdempotencyTests(IServiceFixture databaseServiceFixture, IServiceInitializer databaseInitializer)
    : IdempotencyTestsBase(databaseServiceFixture, databaseInitializer)
{
    [Test]
    [Arguments("negative-offset-key", "2025-01-01T08:00:00.0000000-05:00")]
    [Arguments("positive-offset-key", "2025-01-01T14:30:00.0000000+02:00")]
    public async Task Should_Keep_Live_Non_Utc_Value_On_Reserve(
        string keyName,
        string storedValue,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fakeTime = new FakeTimeProvider();
        fakeTime.AdjustTime(TestDateTime.AddHours(1));

        await RunAndVerify(
                async (services, token) =>
                {
                    var options = services.GetRequiredService<IOptions<IdempotencyKeyOptions>>().Value;
                    var key = $"{options.Schema}:{options.TableName}:{keyName}";
                    var database = services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
                    _ = await database.StringSetAsync(key, storedValue).ConfigureAwait(false);

                    var store = services.GetRequiredService<IIdempotencyStore>();
                    var reserved = await store.TryReserveAsync(keyName, token).ConfigureAwait(false);
                    var value = await database.StringGetAsync(key).ConfigureAwait(false);

                    _ = await Assert.That(reserved).IsFalse();
                    _ = await Assert.That(value.ToString()).IsEqualTo(storedValue);
                },
                cancellationToken,
                configureServices: services =>
                    services
                        .AddSingleton<TimeProvider>(fakeTime)
                        .Configure<IdempotencyKeyOptions>(o => o.TimeToLive = TimeSpan.FromHours(1))
            )
            .ConfigureAwait(false);
    }
}
