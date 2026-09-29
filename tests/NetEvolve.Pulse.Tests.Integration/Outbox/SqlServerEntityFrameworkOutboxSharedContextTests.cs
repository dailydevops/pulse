namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.Outbox;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;

[ClassDataSource<SqlServerDatabaseServiceFixture, EntityFrameworkOutboxInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("SqlServer")]
[TestGroup("EntityFramework")]
[InheritsTests]
public class SqlServerEntityFrameworkOutboxSharedContextTests(
    IServiceFixture databaseServiceFixture,
    IServiceInitializer databaseInitializer
) : EntityFrameworkOutboxSharedContextTestsBase(databaseServiceFixture, databaseInitializer);
