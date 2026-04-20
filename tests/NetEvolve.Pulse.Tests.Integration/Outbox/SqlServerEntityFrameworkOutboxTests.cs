namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;

[ClassDataSource<SqlServerDatabaseServiceFixture, EntityFrameworkOutboxInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("SqlServer")]
[TestGroup("EntityFramework")]
[InheritsTests]
public class SqlServerEntityFrameworkOutboxTests(
    IServiceType databaseServiceFixture,
    IDatabaseInitializer databaseInitializer
) : OutboxTestsBase(databaseServiceFixture, databaseInitializer);
