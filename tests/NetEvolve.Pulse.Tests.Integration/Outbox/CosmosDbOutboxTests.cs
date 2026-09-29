namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
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

    [Test]
    public async Task Should_Keep_Undelivered_Messages_From_Expiring_When_TimeToLive_Is_Enabled(
        CancellationToken cancellationToken
    ) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var options = services.GetRequiredService<IOptions<CosmosDbOutboxOptions>>().Value;
                    var container = services
                        .GetRequiredService<CosmosClient>()
                        .GetContainer(options.DatabaseName, options.ContainerName);
                    var outbox = services.GetRequiredService<IOutboxRepository>();
                    var message = new OutboxMessage
                    {
                        Id = Guid.NewGuid(),
                        EventType = typeof(TtlTestEvent),
                        Payload = "{}",
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow,
                    };
                    var id = message.Id.ToString();

                    await outbox.AddAsync(message, token).ConfigureAwait(false);
                    var added = await container
                        .ReadItemAsync<CosmosDbOutboxDocument>(id, new PartitionKey(id), cancellationToken: token)
                        .ConfigureAwait(false);

                    _ = await outbox.GetPendingAsync(10, token).ConfigureAwait(false);
                    var claimed = await container
                        .ReadItemAsync<CosmosDbOutboxDocument>(id, new PartitionKey(id), cancellationToken: token)
                        .ConfigureAwait(false);

                    await outbox.MarkAsCompletedAsync(message.Id, token).ConfigureAwait(false);
                    var completed = await container
                        .ReadItemAsync<CosmosDbOutboxDocument>(id, new PartitionKey(id), cancellationToken: token)
                        .ConfigureAwait(false);

                    using (Assert.Multiple())
                    {
                        _ = await Assert.That(added.Resource.Ttl).IsEqualTo(-1);
                        _ = await Assert.That(claimed.Resource.Ttl).IsEqualTo(-1);
                        _ = await Assert.That(completed.Resource.Ttl).IsEqualTo(options.TtlSeconds);
                    }
                },
                cancellationToken,
                configureServices: services =>
                    services
                        .Configure<CosmosDbOutboxOptions>(options => options.EnableTimeToLive = true)
                        .Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
            )
            .ConfigureAwait(false);

    private sealed class TtlTestEvent : IEvent
    {
        public string? CausationId { get; set; }

        public string? CorrelationId { get; set; }

        public string Id { get; init; } = Guid.NewGuid().ToString();

        public DateTimeOffset? PublishedAt { get; set; }
    }
}
