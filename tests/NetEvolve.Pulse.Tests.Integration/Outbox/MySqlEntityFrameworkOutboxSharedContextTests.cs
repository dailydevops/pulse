namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.Outbox;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;

[ClassDataSource<MySqlDatabaseServiceFixture, EntityFrameworkOutboxInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("MySql")]
[TestGroup("EntityFramework")]
[InheritsTests]
public class MySqlEntityFrameworkOutboxSharedContextTests(
    IServiceFixture databaseServiceFixture,
    IServiceInitializer databaseInitializer
) : EntityFrameworkOutboxSharedContextTestsBase(databaseServiceFixture, databaseInitializer);
