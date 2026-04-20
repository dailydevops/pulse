namespace NetEvolve.Pulse.Tests.Integration.Idempotency;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;

[ClassDataSource<RedisDatabaseServiceFixture, RedisIdempotencyInitializer>(Shared = [SharedType.None, SharedType.None])]
[TestGroup("Redis")]
[InheritsTests]
public class RedisIdempotencyTests(
    IServiceType databaseServiceFixture,
    IDatabaseInitializer databaseInitializer
) : RedisIdempotencyTestsBase(databaseServiceFixture, databaseInitializer);
