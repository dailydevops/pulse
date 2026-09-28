namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.Outbox;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;

[ClassDataSource<SQLiteDatabaseServiceFixture, EntityFrameworkOutboxInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("SQLite")]
[TestGroup("EntityFramework")]
[InheritsTests]
public class SQLiteEntityFrameworkOutboxClaimRaceTests(
    IServiceFixture databaseServiceFixture,
    IServiceInitializer databaseInitializer
) : EntityFrameworkOutboxClaimRaceTestsBase(databaseServiceFixture, databaseInitializer);
