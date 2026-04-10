namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Tests.Integration.Internals;

[TestGroup("Outbox")]
public abstract class OutboxTestsBase(
    IDatabaseServiceFixture databaseServiceFixture,
    IDatabaseInitializer databaseInitializer
) : PulseTestsBase(databaseServiceFixture, databaseInitializer)
{
    [Test]
    public async Task Should_Persist_ExpectedMessageCount(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    await PublishEventsAsync(mediator, 3, x => new TestEvent { Id = $"Test{x:D3}" }, token);

                    var outbox = services.GetRequiredService<IOutboxRepository>();

                    var result = await outbox.GetPendingCountAsync(token).ConfigureAwait(false);

                    _ = await Assert.That(result).IsEqualTo(3);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_Persist_Expected_Messages(CancellationToken cancellationToken)
    {
        var timeProvider = new FakeTimeProvider();
        timeProvider.AdjustTime(TestDateTime);

        await RunAndVerify(
            async (services, token) =>
            {
                var mediator = services.GetRequiredService<IMediator>();

                await PublishEventsAsync(mediator, 3, x => new TestEvent { Id = $"Test{x:D3}" }, token);

                var outbox = services.GetRequiredService<IOutboxRepository>();
                var result = await outbox.GetPendingAsync(50, token).ConfigureAwait(false);

                _ = await Verify(result.OrderBy(x => x.Payload)).HashParameters().ConfigureAwait(false);
            },
            cancellationToken,
            configureServices: services => services.AddSingleton<TimeProvider>(timeProvider)
        );
    }

    [Test]
    public async Task Should_Return_Zero_PendingCount_When_Empty(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var outbox = services.GetRequiredService<IOutboxRepository>();

                    var result = await outbox.GetPendingCountAsync(token).ConfigureAwait(false);

                    _ = await Assert.That(result).IsEqualTo(0);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_Return_Empty_When_GetPending_NoMessages(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var outbox = services.GetRequiredService<IOutboxRepository>();

                    var result = await outbox.GetPendingAsync(50, token).ConfigureAwait(false);

                    _ = await Assert.That(result).IsEmpty();
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_GetPendingAsync_Respects_BatchSize(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    await PublishEventsAsync(mediator, 5, x => new TestEvent { Id = $"Test{x:D3}" }, token);

                    var outbox = services.GetRequiredService<IOutboxRepository>();
                    var pending = await outbox.GetPendingAsync(3, token).ConfigureAwait(false);

                    _ = await Assert.That(pending.Count).IsEqualTo(3);

                    var remainingCount = await outbox.GetPendingCountAsync(token).ConfigureAwait(false);

                    _ = await Assert.That(remainingCount).IsEqualTo(2);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_Mark_Single_Message_AsCompleted(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    await mediator.PublishAsync(new TestEvent { Id = "Test001" }, token);

                    var outbox = services.GetRequiredService<IOutboxRepository>();
                    var pending = await outbox.GetPendingAsync(50, token).ConfigureAwait(false);

                    _ = await Assert.That(pending.Count).IsEqualTo(1);

                    await outbox.MarkAsCompletedAsync(pending[0].Id, token).ConfigureAwait(false);

                    var pendingCount = await outbox.GetPendingCountAsync(token).ConfigureAwait(false);

                    _ = await Assert.That(pendingCount).IsEqualTo(0);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_Mark_Multiple_Messages_AsCompleted(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    await PublishEventsAsync(mediator, 3, x => new TestEvent { Id = $"Test{x:D3}" }, token);

                    var outbox = services.GetRequiredService<IOutboxRepository>();
                    var pending = await outbox.GetPendingAsync(50, token).ConfigureAwait(false);

                    _ = await Assert.That(pending.Count).IsEqualTo(3);

                    var messageIds = pending.Select(m => m.Id).ToArray();
                    await outbox.MarkAsCompletedAsync(messageIds, token).ConfigureAwait(false);

                    var pendingCount = await outbox.GetPendingCountAsync(token).ConfigureAwait(false);

                    _ = await Assert.That(pendingCount).IsEqualTo(0);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_Mark_Single_Message_AsFailed(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    await mediator.PublishAsync(new TestEvent { Id = "Test001" }, token);

                    var outbox = services.GetRequiredService<IOutboxRepository>();
                    var pending = await outbox.GetPendingAsync(50, token).ConfigureAwait(false);

                    _ = await Assert.That(pending.Count).IsEqualTo(1);

                    await outbox.MarkAsFailedAsync(pending[0].Id, "Test error", token).ConfigureAwait(false);

                    var pendingCount = await outbox.GetPendingCountAsync(token).ConfigureAwait(false);

                    _ = await Assert.That(pendingCount).IsEqualTo(0);

                    var failedForRetry = await outbox.GetFailedForRetryAsync(10, 50, token).ConfigureAwait(false);

                    _ = await Assert.That(failedForRetry.Count).IsEqualTo(1);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_Mark_Multiple_Messages_AsFailed(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    await PublishEventsAsync(mediator, 3, x => new TestEvent { Id = $"Test{x:D3}" }, token);

                    var outbox = services.GetRequiredService<IOutboxRepository>();
                    var pending = await outbox.GetPendingAsync(50, token).ConfigureAwait(false);

                    _ = await Assert.That(pending.Count).IsEqualTo(3);

                    var messageIds = pending.Select(m => m.Id).ToArray();
                    await outbox.MarkAsFailedAsync(messageIds, "Test error", token).ConfigureAwait(false);

                    var pendingCount = await outbox.GetPendingCountAsync(token).ConfigureAwait(false);

                    _ = await Assert.That(pendingCount).IsEqualTo(0);

                    var failedForRetry = await outbox.GetFailedForRetryAsync(10, 50, token).ConfigureAwait(false);

                    _ = await Assert.That(failedForRetry.Count).IsEqualTo(3);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_Mark_Single_Message_AsFailed_WithRetryScheduling(CancellationToken cancellationToken)
    {
        var timeProvider = new FakeTimeProvider();
        timeProvider.AdjustTime(TestDateTime);

        await RunAndVerify(
            async (services, token) =>
            {
                var mediator = services.GetRequiredService<IMediator>();

                await mediator.PublishAsync(new TestEvent { Id = "Test001" }, token);

                var outbox = services.GetRequiredService<IOutboxRepository>();
                var pending = await outbox.GetPendingAsync(50, token).ConfigureAwait(false);

                _ = await Assert.That(pending.Count).IsEqualTo(1);

                await outbox
                    .MarkAsFailedAsync(pending[0].Id, "Test error", TestDateTime.AddHours(1), token)
                    .ConfigureAwait(false);

                var failedForRetry = await outbox.GetFailedForRetryAsync(10, 50, token).ConfigureAwait(false);

                _ = await Assert.That(failedForRetry).IsEmpty();
            },
            cancellationToken,
            configureServices: services => services.AddSingleton<TimeProvider>(timeProvider)
        );
    }

    [Test]
    public async Task Should_Mark_Single_Message_AsDeadLetter(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    await mediator.PublishAsync(new TestEvent { Id = "Test001" }, token);

                    var outbox = services.GetRequiredService<IOutboxRepository>();
                    var pending = await outbox.GetPendingAsync(50, token).ConfigureAwait(false);

                    _ = await Assert.That(pending.Count).IsEqualTo(1);

                    await outbox.MarkAsDeadLetterAsync(pending[0].Id, "Fatal error", token).ConfigureAwait(false);

                    var pendingCount = await outbox.GetPendingCountAsync(token).ConfigureAwait(false);

                    _ = await Assert.That(pendingCount).IsEqualTo(0);

                    var failedForRetry = await outbox.GetFailedForRetryAsync(10, 50, token).ConfigureAwait(false);

                    _ = await Assert.That(failedForRetry).IsEmpty();
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_Mark_Multiple_Messages_AsDeadLetter(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    await PublishEventsAsync(mediator, 3, x => new TestEvent { Id = $"Test{x:D3}" }, token);

                    var outbox = services.GetRequiredService<IOutboxRepository>();
                    var pending = await outbox.GetPendingAsync(50, token).ConfigureAwait(false);

                    _ = await Assert.That(pending.Count).IsEqualTo(3);

                    var messageIds = pending.Select(m => m.Id).ToArray();
                    await outbox.MarkAsDeadLetterAsync(messageIds, "Fatal error", token).ConfigureAwait(false);

                    var pendingCount = await outbox.GetPendingCountAsync(token).ConfigureAwait(false);

                    _ = await Assert.That(pendingCount).IsEqualTo(0);

                    var failedForRetry = await outbox.GetFailedForRetryAsync(10, 50, token).ConfigureAwait(false);

                    _ = await Assert.That(failedForRetry).IsEmpty();
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_GetFailedForRetry_ExcludesScheduledMessages(CancellationToken cancellationToken)
    {
        var timeProvider = new FakeTimeProvider();
        timeProvider.AdjustTime(TestDateTime);

        await RunAndVerify(
            async (services, token) =>
            {
                var mediator = services.GetRequiredService<IMediator>();

                await PublishEventsAsync(mediator, 2, x => new TestEvent { Id = $"Test{x:D3}" }, token);

                var outbox = services.GetRequiredService<IOutboxRepository>();
                var pending = await outbox.GetPendingAsync(50, token).ConfigureAwait(false);

                _ = await Assert.That(pending.Count).IsEqualTo(2);

                await outbox
                    .MarkAsFailedAsync(pending[0].Id, "Scheduled error", TestDateTime.AddHours(1), token)
                    .ConfigureAwait(false);
                await outbox.MarkAsFailedAsync(pending[1].Id, "Immediate error", token).ConfigureAwait(false);

                var failedForRetry = await outbox.GetFailedForRetryAsync(10, 50, token).ConfigureAwait(false);

                _ = await Assert.That(failedForRetry.Count).IsEqualTo(1);
            },
            cancellationToken,
            configureServices: services => services.AddSingleton<TimeProvider>(timeProvider)
        );
    }

    [Test]
    public async Task Should_DeleteCompleted_ReturnsCorrectCount(CancellationToken cancellationToken)
    {
        var timeProvider = new FakeTimeProvider();
        timeProvider.AdjustTime(TestDateTime);

        await RunAndVerify(
            async (services, token) =>
            {
                var mediator = services.GetRequiredService<IMediator>();

                await PublishEventsAsync(mediator, 3, x => new TestEvent { Id = $"Test{x:D3}" }, token);

                var outbox = services.GetRequiredService<IOutboxRepository>();
                var pending = await outbox.GetPendingAsync(50, token).ConfigureAwait(false);

                _ = await Assert.That(pending.Count).IsEqualTo(3);

                var messageIds = pending.Select(m => m.Id).ToArray();
                await outbox.MarkAsCompletedAsync(messageIds, token).ConfigureAwait(false);

                timeProvider.Advance(TimeSpan.FromMinutes(1));

                var deleted = await outbox.DeleteCompletedAsync(TimeSpan.FromSeconds(30), token).ConfigureAwait(false);

                _ = await Assert.That(deleted).IsEqualTo(3);
            },
            cancellationToken,
            configureServices: services => services.AddSingleton<TimeProvider>(timeProvider)
        );
    }

    [Test]
    public async Task Should_GetPendingAsync_ExcludesProcessingMessages(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    await PublishEventsAsync(mediator, 3, x => new TestEvent { Id = $"Test{x:D3}" }, token);

                    var outbox = services.GetRequiredService<IOutboxRepository>();
                    _ = await outbox.GetPendingAsync(50, token).ConfigureAwait(false);

                    var secondBatch = await outbox.GetPendingAsync(50, token).ConfigureAwait(false);

                    _ = await Assert.That(secondBatch).IsEmpty();
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_GetFailedForRetry_Returns_Empty_When_NoFailedMessages(
        CancellationToken cancellationToken
    ) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var outbox = services.GetRequiredService<IOutboxRepository>();

                    var result = await outbox.GetFailedForRetryAsync(10, 50, token).ConfigureAwait(false);

                    _ = await Assert.That(result).IsEmpty();
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_GetFailedForRetry_Excludes_MaxRetryCount_Exceeded(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    await mediator.PublishAsync(new TestEvent { Id = "Test001" }, token);

                    var outbox = services.GetRequiredService<IOutboxRepository>();
                    var pending = await outbox.GetPendingAsync(50, token).ConfigureAwait(false);

                    _ = await Assert.That(pending.Count).IsEqualTo(1);

                    await outbox.MarkAsFailedAsync(pending[0].Id, "Error 1", token).ConfigureAwait(false);

                    var firstRetry = await outbox.GetFailedForRetryAsync(3, 50, token).ConfigureAwait(false);

                    _ = await Assert.That(firstRetry.Count).IsEqualTo(1);

                    await outbox.MarkAsFailedAsync(firstRetry[0].Id, "Error 2", token).ConfigureAwait(false);

                    var secondRetry = await outbox.GetFailedForRetryAsync(2, 50, token).ConfigureAwait(false);

                    _ = await Assert.That(secondRetry).IsEmpty();
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_DeleteCompleted_DoesNotDelete_NonCompletedMessages(CancellationToken cancellationToken)
    {
        var timeProvider = new FakeTimeProvider();
        timeProvider.AdjustTime(TestDateTime);

        await RunAndVerify(
            async (services, token) =>
            {
                var mediator = services.GetRequiredService<IMediator>();

                await PublishEventsAsync(mediator, 4, x => new TestEvent { Id = $"Test{x:D3}" }, token);

                var outbox = services.GetRequiredService<IOutboxRepository>();
                var pending = await outbox.GetPendingAsync(50, token).ConfigureAwait(false);

                _ = await Assert.That(pending.Count).IsEqualTo(4);

                var completedIds = pending.Take(2).Select(m => m.Id).ToArray();
                await outbox.MarkAsCompletedAsync(completedIds, token).ConfigureAwait(false);

                var failedIds = pending.Skip(2).Select(m => m.Id).ToArray();
                await outbox.MarkAsFailedAsync(failedIds, "Test error", token).ConfigureAwait(false);

                timeProvider.Advance(TimeSpan.FromMinutes(1));

                var deleted = await outbox.DeleteCompletedAsync(TimeSpan.FromSeconds(30), token).ConfigureAwait(false);

                _ = await Assert.That(deleted).IsEqualTo(2);

                var failedForRetry = await outbox.GetFailedForRetryAsync(10, 50, token).ConfigureAwait(false);

                _ = await Assert.That(failedForRetry.Count).IsEqualTo(2);
            },
            cancellationToken,
            configureServices: services => services.AddSingleton<TimeProvider>(timeProvider)
        );
    }

    private sealed class TestEvent : IEvent
    {
        public string? CorrelationId { get; set; }

        public required string Id { get; init; }

        public DateTimeOffset? PublishedAt { get; set; }
    }
}
