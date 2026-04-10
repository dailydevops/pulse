namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;

[ClassDataSource<InMemoryDatabaseServiceFixture, EntityFrameworkInitializer>(
    Shared = [SharedType.None, SharedType.PerTestSession]
)]
[TestGroup("InMemory")]
[TestGroup("EntityFramework")]
[InheritsTests]
public class InMemoryEntityFrameworkOutboxTests(
    IDatabaseServiceFixture databaseServiceFixture,
    IDatabaseInitializer databaseInitializer
) : OutboxTestsBase(databaseServiceFixture, databaseInitializer);
