namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;

[ClassDataSource<SQLiteDatabaseServiceFixture, EntityFrameworkInitializer>(
    Shared = [SharedType.None, SharedType.PerTestSession]
)]
[TestGroup("SQLite")]
[TestGroup("EntityFramework")]
[InheritsTests]
public class SQLiteEntityFrameworkOutboxTests(
    IDatabaseServiceFixture databaseServiceFixture,
    IDatabaseInitializer databaseInitializer
) : OutboxTestsBase(databaseServiceFixture, databaseInitializer);
