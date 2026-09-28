namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Outbox;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.Outbox;
using NetEvolve.Pulse.Tests.Integration.Internals.Services;

[ClassDataSource<CosmosDbDatabaseServiceFixture, CosmosDbOutboxInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("CosmosDb")]
[InheritsTests]
public class CosmosDbOutboxTests(IServiceFixture databaseServiceFixture, IServiceInitializer databaseInitializer)
    : OutboxTestsBase(databaseServiceFixture, databaseInitializer)
{
    /// <inheritdoc />
    /// <remarks>Cosmos DB dead-letter paging has no <c>Id</c> tie-breaker yet (tracked in #793).</remarks>
    protected override bool OrdersDeadLettersById => false;

    [Test]
    public async Task Should_Reject_Container_With_Unsupported_PartitionKeyPath(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var testableCodeRan = false;

        var exception = await Assert
            .That(async () =>
                await RunAndVerify(
                        (_, _) =>
                        {
                            testableCodeRan = true;
                            return Task.CompletedTask;
                        },
                        cancellationToken,
                        configureServices: services =>
                            services
                                .Configure<CosmosDbOutboxOptions>(options => options.PartitionKeyPath = "/eventType")
                                .Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
                    )
                    .ConfigureAwait(false)
            )
            .ThrowsExactly<OptionsValidationException>();

        using (Assert.Multiple())
        {
            _ = await Assert.That(exception!.Message).Contains(nameof(CosmosDbOutboxOptions.PartitionKeyPath));
            _ = await Assert.That(testableCodeRan).IsFalse();
        }
    }
}
