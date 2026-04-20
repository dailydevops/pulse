namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;

[ClassDataSource<SqlServerDatabaseServiceFixture, SqlServerAdoNetOutboxInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("SqlServer")]
[TestGroup("AdoNet")]
[InheritsTests]
public class SqlServerAdoNetOutboxTests(
    IServiceType databaseServiceFixture,
    IDatabaseInitializer databaseInitializer
) : OutboxTestsBase(databaseServiceFixture, databaseInitializer);
