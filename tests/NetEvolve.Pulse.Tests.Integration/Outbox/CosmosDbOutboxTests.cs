namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using NetEvolve.Pulse.Tests.Integration.Internals;

[ClassDataSource<CosmosDbDatabaseServiceFixture, CosmosDbOutboxInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("CosmosDb")]
[InheritsTests]
public class CosmosDbOutboxTests(IServiceFixture databaseServiceFixture, IServiceInitializer databaseInitializer)
    : OutboxTestsBase(databaseServiceFixture, databaseInitializer)
{
    [Test]
    public async Task Should_Reject_Container_With_Unsupported_PartitionKeyPath(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    _ = await Assert
                        .That(() => services.GetRequiredService<IOutboxRepository>())
                        .Throws<ArgumentException>();
                    _ = await Assert
                        .That(() => services.GetRequiredService<IOutboxManagement>())
                        .Throws<ArgumentException>();
                },
                cancellationToken,
                configureServices: services =>
                    services
                        .Configure<CosmosDbOutboxOptions>(options => options.PartitionKeyPath = "/eventType")
                        .Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
            )
            .ConfigureAwait(false);
}
