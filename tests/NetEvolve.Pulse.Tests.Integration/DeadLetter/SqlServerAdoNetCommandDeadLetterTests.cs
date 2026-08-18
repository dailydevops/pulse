namespace NetEvolve.Pulse.Tests.Integration.DeadLetter;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.DeadLetter;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;

[ClassDataSource<SqlServerDatabaseServiceFixture, SqlServerAdoNetCommandDeadLetterInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("SqlServer")]
[TestGroup("AdoNet")]
[InheritsTests]
public class SqlServerAdoNetCommandDeadLetterTests(
    IServiceFixture databaseServiceFixture,
    IServiceInitializer databaseInitializer
) : CommandDeadLetterTestsBase(databaseServiceFixture, databaseInitializer);
