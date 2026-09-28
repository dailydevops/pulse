namespace NetEvolve.Pulse.Tests.Integration.Audit;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.Audit;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;

[ClassDataSource<SQLiteDatabaseServiceFixture, SQLiteAdoNetAuditInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("SQLite")]
[TestGroup("AdoNet")]
[InheritsTests]
public class SQLiteAdoNetAuditTests(IServiceFixture databaseServiceFixture, IServiceInitializer databaseInitializer)
    : AuditTestsBase(databaseServiceFixture, databaseInitializer);
