namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;

[ClassDataSource<MySqlDatabaseServiceFixture, MySqlAdoNetOutboxInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("MySql")]
[TestGroup("AdoNet")]
[InheritsTests]
public class MySqlAdoNetOutboxTests(IServiceFixture databaseServiceFixture, IDatabaseInitializer databaseInitializer)
    : OutboxTestsBase(databaseServiceFixture, databaseInitializer);
