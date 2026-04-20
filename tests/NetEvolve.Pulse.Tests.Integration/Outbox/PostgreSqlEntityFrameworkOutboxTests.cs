namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.Outbox;

[ClassDataSource<PostgreSqlDatabaseServiceFixture, EntityFrameworkOutboxInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("PostgreSql")]
[TestGroup("EntityFramework")]
[InheritsTests]
public class PostgreSqlEntityFrameworkOutboxTests(
    IServiceFixture databaseServiceFixture,
    IServiceInitializer databaseInitializer
) : OutboxTestsBase(databaseServiceFixture, databaseInitializer);
