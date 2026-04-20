namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.Outbox;

[ClassDataSource<MySqlDatabaseServiceFixture, MySqlAdoNetOutboxInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("MySql")]
[TestGroup("AdoNet")]
[InheritsTests]
public class MySqlAdoNetOutboxTests(IServiceFixture databaseServiceFixture, IServiceInitializer databaseInitializer)
    : OutboxTestsBase(databaseServiceFixture, databaseInitializer);
