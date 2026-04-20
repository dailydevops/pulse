namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.Outbox;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;

[ClassDataSource<InMemoryDatabaseServiceFixture, EntityFrameworkOutboxInitializer>(
    Shared = [SharedType.None, SharedType.PerTestSession]
)]
[TestGroup("InMemory")]
[TestGroup("EntityFramework")]
[InheritsTests]
public class InMemoryEntityFrameworkOutboxTests(
    IServiceFixture databaseServiceFixture,
    IServiceInitializer databaseInitializer
) : OutboxTestsBase(databaseServiceFixture, databaseInitializer);
