namespace NetEvolve.Pulse.Tests.Integration.EntityFramework;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;

[TestGroup("Outbox")]
[ClassDataSource<SqlServerDatabaseServiceFixture, AdoNetDatabaseInitializer>(
    Shared = [SharedType.PerClass, SharedType.PerTestSession]
)]
[ClassDataSource<SqlServerDatabaseServiceFixture, EntityFrameworkInitializer>(
    Shared = [SharedType.PerClass, SharedType.PerTestSession]
)]
public class OutboxTests
{
    public IDatabaseServiceFixture DatabaseServiceFixture { get; }
    public IDatabaseInitializer DatabaseInitializer { get; }

    public OutboxTests(IDatabaseServiceFixture databaseServiceFixture, IDatabaseInitializer databaseInitializer)
    {
        DatabaseServiceFixture = databaseServiceFixture;
        DatabaseInitializer = databaseInitializer;
    }

    [Test]
    [CombinedDataSources]
    public async Task Can_Combine_Outbox_Setup()
    {
        await Assert.That(DatabaseServiceFixture).IsNotNull();
        await Assert.That(DatabaseInitializer).IsNotNull();
    }
}
