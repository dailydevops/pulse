namespace NetEvolve.Pulse.Tests.Integration.Idempotency;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.Idempotency;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;

[ClassDataSource<RedisServiceFixture, RedisIdempotencyInitializer>(Shared = [SharedType.None, SharedType.None])]
[TestGroup("Redis")]
[InheritsTests]
public class RedisIdempotencyTests(IServiceFixture databaseServiceFixture, IServiceInitializer databaseInitializer)
    : IdempotencyTestsBase(databaseServiceFixture, databaseInitializer);
