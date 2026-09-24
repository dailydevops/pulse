namespace NetEvolve.Pulse.Tests.Integration.Audit;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.Audit;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;

[ClassDataSource<SQLiteDatabaseServiceFixture, EntityFrameworkAuditInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("SQLite")]
[TestGroup("EntityFramework")]
[InheritsTests]
public class SQLiteEntityFrameworkAuditTests(
    IServiceFixture databaseServiceFixture,
    IServiceInitializer databaseInitializer
) : AuditTestsBase(databaseServiceFixture, databaseInitializer);
