namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Tests.Integration.Internals;

[ClassDataSource<MongoDbDatabaseServiceFixture, MongoDbOutboxInitializer>(Shared = [SharedType.None, SharedType.None])]
[TestGroup("MongoDB")]
[InheritsTests]
public class MongoDbOutboxTests(
    IDatabaseServiceFixture databaseServiceFixture,
    IDatabaseInitializer databaseInitializer
) : OutboxTestsBase(databaseServiceFixture, databaseInitializer);
