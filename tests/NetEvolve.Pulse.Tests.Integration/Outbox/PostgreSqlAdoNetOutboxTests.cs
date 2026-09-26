namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using NetEvolve.Pulse.Tests.Integration.Internals;

[ClassDataSource<PostgreSqlDatabaseServiceFixture, PostgreSqlAdoNetOutboxInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("PostgreSql")]
[TestGroup("AdoNet")]
[InheritsTests]
public class PostgreSqlAdoNetOutboxTests(
    IServiceFixture databaseServiceFixture,
    IServiceInitializer databaseInitializer
) : OutboxTestsBase(databaseServiceFixture, databaseInitializer)
{
    [Test]
    public async Task Should_Mark_Multiple_Messages_AsCompleted_OnlyProcessingMessages(
        CancellationToken cancellationToken
    ) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    await PublishEventsAsync(mediator, 3, x => new BatchTestEvent { Id = $"Test{x:D3}" }, token)
                        .ConfigureAwait(false);

                    var outbox = services.GetRequiredService<IOutboxRepository>();
                    var processing = await outbox.GetPendingAsync(2, token).ConfigureAwait(false);

                    _ = await Assert.That(processing.Count).IsEqualTo(2);

                    var pendingCountBefore = await outbox.GetPendingCountAsync(token).ConfigureAwait(false);

                    _ = await Assert.That(pendingCountBefore).IsEqualTo(1);

                    var messageIds = processing.Select(m => m.Id).ToArray();
                    await outbox.MarkAsCompletedAsync(messageIds, token).ConfigureAwait(false);

                    var management = services.GetRequiredService<IOutboxManagement>();
                    var statistics = await management.GetStatisticsAsync(token).ConfigureAwait(false);

                    using (Assert.Multiple())
                    {
                        _ = await Assert.That(statistics.Completed).IsEqualTo(2L);
                        _ = await Assert.That(statistics.Processing).IsEqualTo(0L);
                        _ = await Assert.That(statistics.Pending).IsEqualTo(1L);
                    }
                },
                cancellationToken,
                configureServices: services =>
                    services.Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_Mark_Multiple_Messages_AsFailed_IncrementsRetryCountPerMessage(
        CancellationToken cancellationToken
    ) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    await PublishEventsAsync(mediator, 3, x => new BatchTestEvent { Id = $"Test{x:D3}" }, token)
                        .ConfigureAwait(false);

                    var outbox = services.GetRequiredService<IOutboxRepository>();
                    var pending = await outbox.GetPendingAsync(50, token).ConfigureAwait(false);

                    _ = await Assert.That(pending.Count).IsEqualTo(3);

                    var messageIds = pending.Select(m => m.Id).ToArray();
                    await outbox.MarkAsFailedAsync(messageIds, "Batch error", token).ConfigureAwait(false);

                    var failedForRetry = await outbox.GetFailedForRetryAsync(10, 50, token).ConfigureAwait(false);

                    using (Assert.Multiple())
                    {
                        _ = await Assert.That(failedForRetry.Count).IsEqualTo(3);
                        _ = await Assert.That(failedForRetry.All(m => m.RetryCount == 1)).IsTrue();
                        _ = await Assert
                            .That(
                                failedForRetry.All(m => string.Equals(m.Error, "Batch error", StringComparison.Ordinal))
                            )
                            .IsTrue();
                        _ = await Assert.That(failedForRetry.All(m => m.NextRetryAt is null)).IsTrue();
                    }
                },
                cancellationToken,
                configureServices: services =>
                    services.Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_Mark_Multiple_Messages_AsFailed_OnlyProcessingMessages(
        CancellationToken cancellationToken
    ) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    await PublishEventsAsync(mediator, 3, x => new BatchTestEvent { Id = $"Test{x:D3}" }, token)
                        .ConfigureAwait(false);

                    var outbox = services.GetRequiredService<IOutboxRepository>();
                    var processing = await outbox.GetPendingAsync(2, token).ConfigureAwait(false);

                    _ = await Assert.That(processing.Count).IsEqualTo(2);

                    var messageIds = processing.Select(m => m.Id).ToArray();
                    await outbox.MarkAsFailedAsync(messageIds, "Batch error", token).ConfigureAwait(false);

                    var management = services.GetRequiredService<IOutboxManagement>();
                    var statistics = await management.GetStatisticsAsync(token).ConfigureAwait(false);

                    using (Assert.Multiple())
                    {
                        _ = await Assert.That(statistics.Failed).IsEqualTo(2L);
                        _ = await Assert.That(statistics.Processing).IsEqualTo(0L);
                        _ = await Assert.That(statistics.Pending).IsEqualTo(1L);
                    }
                },
                cancellationToken,
                configureServices: services =>
                    services.Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_Mark_Single_Message_AsFailed_With_NonUtc_NextRetryAt(
        CancellationToken cancellationToken
    ) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();
                    await mediator.PublishAsync(new BatchTestEvent { Id = "Test001" }, token).ConfigureAwait(false);

                    var outbox = services.GetRequiredService<IOutboxRepository>();
                    var pending = await outbox.GetPendingAsync(50, token).ConfigureAwait(false);
                    var nextRetryAt = new DateTimeOffset(2025, 1, 1, 14, 0, 0, TimeSpan.FromHours(2));

                    await outbox
                        .MarkAsFailedAsync(pending[0].Id, "Test error", nextRetryAt, token)
                        .ConfigureAwait(false);

                    var management = services.GetRequiredService<IOutboxManagement>();
                    var message = await management.GetMessageAsync(pending[0].Id, token).ConfigureAwait(false);

                    _ = await Assert.That(message).IsNotNull();
                    _ = await Assert.That(message!.NextRetryAt).IsEqualTo(nextRetryAt);
                },
                cancellationToken,
                configureServices: services =>
                    services.Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_Add_Message_With_NonUtc_Timestamps(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var timestamp = new DateTimeOffset(2025, 1, 1, 14, 0, 0, TimeSpan.FromHours(2));
                    var message = new OutboxMessage
                    {
                        Id = Guid.NewGuid(),
                        EventType = typeof(BatchTestEvent),
                        Payload = "{}",
                        CreatedAt = timestamp,
                        UpdatedAt = timestamp,
                        ProcessedAt = timestamp,
                        NextRetryAt = timestamp,
                        Status = OutboxMessageStatus.Failed,
                    };

                    var outbox = services.GetRequiredService<IOutboxRepository>();
                    await outbox.AddAsync(message, token).ConfigureAwait(false);

                    var management = services.GetRequiredService<IOutboxManagement>();
                    var persisted = await management.GetMessageAsync(message.Id, token).ConfigureAwait(false);

                    _ = await Assert.That(persisted).IsNotNull();
                    using (Assert.Multiple())
                    {
                        _ = await Assert.That(persisted!.CreatedAt).IsEqualTo(timestamp);
                        _ = await Assert.That(persisted.UpdatedAt).IsEqualTo(timestamp);
                        _ = await Assert.That(persisted.ProcessedAt).IsEqualTo(timestamp);
                        _ = await Assert.That(persisted.NextRetryAt).IsEqualTo(timestamp);
                    }
                },
                cancellationToken,
                configureServices: services =>
                    services.Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
            )
            .ConfigureAwait(false);

    private sealed class BatchTestEvent : IEvent
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }

        public required string Id { get; init; }

        public DateTimeOffset? PublishedAt { get; set; }
    }
}
