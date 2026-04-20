namespace NetEvolve.Pulse.Tests.Integration.Idempotency;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.Idempotency;

[ClassDataSource<PostgreSqlDatabaseServiceFixture, EntityFrameworkIdempotencyInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("PostgreSql")]
[TestGroup("EntityFramework")]
[InheritsTests]
public class PostgreSqlEntityFrameworkIdempotencyTests(
    IServiceFixture databaseServiceFixture,
    IServiceInitializer databaseInitializer
) : IdempotencyTestsBase(databaseServiceFixture, databaseInitializer);
