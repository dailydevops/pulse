namespace NetEvolve.Pulse.Tests.Integration.Outbox;

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
    public async Task Should_ReplayMessage_Clear_Stale_ProcessedAt(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var messageId = await AddStaleDeadLetterAsync(services, token).ConfigureAwait(false);

                    var management = services.GetRequiredService<IOutboxManagement>();
                    var replayed = await management.ReplayMessageAsync(messageId, token).ConfigureAwait(false);

                    _ = await Assert.That(replayed).IsTrue();
                    await AssertReplayedAsync(management, messageId, token).ConfigureAwait(false);
                },
                cancellationToken,
                configureServices: services =>
                    services.Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_ReplayAllDeadLetter_Clear_Stale_ProcessedAt(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var messageId = await AddStaleDeadLetterAsync(services, token).ConfigureAwait(false);

                    var management = services.GetRequiredService<IOutboxManagement>();
                    var count = await management.ReplayAllDeadLetterAsync(token).ConfigureAwait(false);

                    _ = await Assert.That(count).IsEqualTo(1);
                    await AssertReplayedAsync(management, messageId, token).ConfigureAwait(false);
                },
                cancellationToken,
                configureServices: services =>
                    services.Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
            )
            .ConfigureAwait(false);

    /// <summary>
    /// Persists a dead-letter document that carries <c>processedAt</c> and <c>nextRetryAt</c> values,
    /// as documents dead-lettered by earlier versions do.
    /// </summary>
    private static async Task<Guid> AddStaleDeadLetterAsync(
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = typeof(TestEvent),
            Payload = "{}",
            CreatedAt = now,
            UpdatedAt = now,
            ProcessedAt = now,
            NextRetryAt = now,
            RetryCount = 3,
            Error = "Fatal error",
            Status = OutboxMessageStatus.DeadLetter,
        };

        var outbox = services.GetRequiredService<IOutboxRepository>();
        await outbox.AddAsync(message, cancellationToken).ConfigureAwait(false);

        return message.Id;
    }

    private static async Task AssertReplayedAsync(
        IOutboxManagement management,
        Guid messageId,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var message = await management.GetMessageAsync(messageId, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(message).IsNotNull();
        using (Assert.Multiple())
        {
            _ = await Assert.That(message!.Status).IsEqualTo(OutboxMessageStatus.Pending);
            _ = await Assert.That(message.ProcessedAt).IsNull();
            _ = await Assert.That(message.NextRetryAt).IsNull();
            _ = await Assert.That(message.RetryCount).IsEqualTo(0);
        }
    }

    private sealed class TestEvent : IEvent
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }

        public required string Id { get; init; }

        public DateTimeOffset? PublishedAt { get; set; }
    }
}
