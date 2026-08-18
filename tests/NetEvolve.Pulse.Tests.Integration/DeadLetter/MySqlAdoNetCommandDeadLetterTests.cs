namespace NetEvolve.Pulse.Tests.Integration.DeadLetter;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.DeadLetter;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;

[ClassDataSource<MySqlDatabaseServiceFixture, MySqlAdoNetCommandDeadLetterInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("MySql")]
[TestGroup("AdoNet")]
[InheritsTests]
public class MySqlAdoNetCommandDeadLetterTests(
    IServiceFixture databaseServiceFixture,
    IServiceInitializer databaseInitializer
) : CommandDeadLetterTestsBase(databaseServiceFixture, databaseInitializer);
