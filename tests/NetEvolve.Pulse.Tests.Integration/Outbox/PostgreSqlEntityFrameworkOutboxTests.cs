namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;

[ClassDataSource<PostgreSqlDatabaseServiceFixture, EntityFrameworkOutboxInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("PostgreSql")]
[TestGroup("EntityFramework")]
[InheritsTests]
public class PostgreSqlEntityFrameworkOutboxTests(
    IServiceType databaseServiceFixture,
    IDatabaseInitializer databaseInitializer
) : OutboxTestsBase(databaseServiceFixture, databaseInitializer);
