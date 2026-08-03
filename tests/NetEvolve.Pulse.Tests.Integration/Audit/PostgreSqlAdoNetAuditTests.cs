namespace NetEvolve.Pulse.Tests.Integration.Audit;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.Audit;

[ClassDataSource<PostgreSqlDatabaseServiceFixture, PostgreSqlAdoNetAuditInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("PostgreSql")]
[TestGroup("AdoNet")]
[InheritsTests]
public class PostgreSqlAdoNetAuditTests(IServiceFixture databaseServiceFixture, IServiceInitializer databaseInitializer)
    : AuditTestsBase(databaseServiceFixture, databaseInitializer);
