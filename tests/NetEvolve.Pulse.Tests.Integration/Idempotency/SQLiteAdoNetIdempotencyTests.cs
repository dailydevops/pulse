namespace NetEvolve.Pulse.Tests.Integration.Idempotency;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;

[ClassDataSource<SQLiteDatabaseServiceFixture, SQLiteAdoNetIdempotencyInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("SQLite")]
[TestGroup("AdoNet")]
[InheritsTests]
public class SQLiteAdoNetIdempotencyTests(
    IServiceType databaseServiceFixture,
    IDatabaseInitializer databaseInitializer
) : IdempotencyTestsBase(databaseServiceFixture, databaseInitializer);
