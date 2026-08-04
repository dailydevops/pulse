namespace NetEvolve.Pulse.Tests.Integration.Audit;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.Audit;

[ClassDataSource<MySqlDatabaseServiceFixture, EntityFrameworkAuditInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("MySql")]
[TestGroup("EntityFramework")]
[InheritsTests]
public class MySqlEntityFrameworkAuditTests(
    IServiceFixture databaseServiceFixture,
    IServiceInitializer databaseInitializer
) : AuditTestsBase(databaseServiceFixture, databaseInitializer);
