namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.Outbox;

[ClassDataSource<SQLiteDatabaseServiceFixture, EntityFrameworkOutboxInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("SQLite")]
[TestGroup("EntityFramework")]
[InheritsTests]
public class SQLiteEntityFrameworkOutboxTests(
    IServiceFixture databaseServiceFixture,
    IServiceInitializer databaseInitializer
) : OutboxTestsBase(databaseServiceFixture, databaseInitializer);
