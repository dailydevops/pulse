namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.Outbox;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;

[ClassDataSource<PostgreSqlDatabaseServiceFixture, EntityFrameworkOutboxInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("PostgreSql")]
[TestGroup("EntityFramework")]
[InheritsTests]
public class PostgreSqlEntityFrameworkOutboxClaimRaceTests(
    IServiceFixture databaseServiceFixture,
    IServiceInitializer databaseInitializer
) : EntityFrameworkOutboxClaimRaceTestsBase(databaseServiceFixture, databaseInitializer);
