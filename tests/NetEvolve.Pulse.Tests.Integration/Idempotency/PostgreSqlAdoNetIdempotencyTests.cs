namespace NetEvolve.Pulse.Tests.Integration.Idempotency;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;

[ClassDataSource<PostgreSqlDatabaseServiceFixture, PostgreSqlAdoNetIdempotencyInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("PostgreSql")]
[TestGroup("AdoNet")]
[InheritsTests]
public class PostgreSqlAdoNetIdempotencyTests(
    IServiceFixture databaseServiceFixture,
    IServiceInitializer databaseInitializer
) : IdempotencyTestsBase(databaseServiceFixture, databaseInitializer);
