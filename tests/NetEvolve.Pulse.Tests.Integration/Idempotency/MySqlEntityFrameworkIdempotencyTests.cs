namespace NetEvolve.Pulse.Tests.Integration.Idempotency;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;

[ClassDataSource<MySqlDatabaseServiceFixture, EntityFrameworkIdempotencyInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("MySql")]
[TestGroup("EntityFramework")]
[InheritsTests]
public class MySqlEntityFrameworkIdempotencyTests(
    IServiceType databaseServiceFixture,
    IDatabaseInitializer databaseInitializer
) : IdempotencyTestsBase(databaseServiceFixture, databaseInitializer);
