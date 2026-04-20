namespace NetEvolve.Pulse.Tests.Integration.Idempotency;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;

[ClassDataSource<PostgreSqlDatabaseServiceFixture, PostgreSqlAdoNetIdempotencyInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("PostgreSql")]
[TestGroup("AdoNet")]
[InheritsTests]
public class PostgreSqlAdoNetIdempotencyTests(
    IServiceType databaseServiceFixture,
    IDatabaseInitializer databaseInitializer
) : IdempotencyTestsBase(databaseServiceFixture, databaseInitializer);
