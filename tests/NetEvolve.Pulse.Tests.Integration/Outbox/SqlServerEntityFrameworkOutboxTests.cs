namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;

[ClassDataSource<SqlServerDatabaseServiceFixture, EntityFrameworkInitializer>(
    Shared = [SharedType.PerClass, SharedType.PerClass]
)]
[TestGroup("SqlServer")]
[TestGroup("EntityFramework")]
[InheritsTests]
public class SqlServerEntityFrameworkOutboxTests(
    IDatabaseServiceFixture databaseServiceFixture,
    IDatabaseInitializer databaseInitializer
) : OutboxTestsBase(databaseServiceFixture, databaseInitializer);
