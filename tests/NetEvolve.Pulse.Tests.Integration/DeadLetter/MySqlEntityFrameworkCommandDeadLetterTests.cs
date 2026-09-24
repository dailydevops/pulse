namespace NetEvolve.Pulse.Tests.Integration.DeadLetter;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.DeadLetter;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;

[ClassDataSource<MySqlDatabaseServiceFixture, EntityFrameworkCommandDeadLetterInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("MySql")]
[TestGroup("EntityFramework")]
[InheritsTests]
public class MySqlEntityFrameworkCommandDeadLetterTests(
    IServiceFixture databaseServiceFixture,
    IServiceInitializer databaseInitializer
) : CommandDeadLetterTestsBase(databaseServiceFixture, databaseInitializer);
