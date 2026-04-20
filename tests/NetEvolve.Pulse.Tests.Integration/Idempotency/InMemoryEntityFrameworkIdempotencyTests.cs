namespace NetEvolve.Pulse.Tests.Integration.Idempotency;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;

[ClassDataSource<InMemoryDatabaseServiceFixture, EntityFrameworkIdempotencyInitializer>(
    Shared = [SharedType.None, SharedType.PerTestSession]
)]
[TestGroup("InMemory")]
[TestGroup("EntityFramework")]
[InheritsTests]
public class InMemoryEntityFrameworkIdempotencyTests(
    IServiceFixture databaseServiceFixture,
    IDatabaseInitializer databaseInitializer
) : IdempotencyTestsBase(databaseServiceFixture, databaseInitializer);
