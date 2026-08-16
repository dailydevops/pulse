namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;

[ClassDataSource<SqlServerDatabaseServiceFixture, SqlServerAdoNetOutboxInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("SqlServer")]
[TestGroup("AdoNet")]
[InheritsTests]
public class SqlServerAdoNetOutboxTests(IServiceFixture databaseServiceFixture, IServiceInitializer databaseInitializer)
    : OutboxTestsBase(databaseServiceFixture, databaseInitializer);
