namespace NetEvolve.Pulse.Tests.Integration.Idempotency;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;

[ClassDataSource<SqlServerDatabaseServiceFixture, SqlServerAdoNetIdempotencyInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("SqlServer")]
[TestGroup("AdoNet")]
[InheritsTests]
public class SqlServerAdoNetIdempotencyTests(
    IDatabaseServiceFixture databaseServiceFixture,
    IDatabaseInitializer databaseInitializer
) : IdempotencyTestsBase(databaseServiceFixture, databaseInitializer);
