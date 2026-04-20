namespace NetEvolve.Pulse.Tests.Integration.Idempotency;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;

[ClassDataSource<SQLiteDatabaseServiceFixture, EntityFrameworkIdempotencyInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("SQLite")]
[TestGroup("EntityFramework")]
[InheritsTests]
public class SQLiteEntityFrameworkIdempotencyTests(
    IServiceType databaseServiceFixture,
    IDatabaseInitializer databaseInitializer
) : IdempotencyTestsBase(databaseServiceFixture, databaseInitializer);
