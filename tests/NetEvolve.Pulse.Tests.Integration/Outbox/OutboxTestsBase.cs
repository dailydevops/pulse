namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using NetEvolve.Pulse.Tests.Integration.Internals;

[TestGroup("Outbox")]
[Timeout(300_000)] // Increased timeout to accommodate potential delays in CI environments, especially when using SQL Server or MySQL containers that can take a long time to cold-start.
public abstract class OutboxTestsBase(IServiceFixture databaseServiceFixture, IDatabaseInitializer databaseInitializer)
    : PulseTestsBase(databaseServiceFixture, databaseInitializer)
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
                cancellationToken,
                configureServices: services =>
                    services.Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
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
            configureServices: services =>
                services
                    .AddSingleton<TimeProvider>(timeProvider)
                    .Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
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
                cancellationToken,
                configureServices: services =>
                    services.Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
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
                cancellationToken,
                configureServices: services =>
                    services.Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
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
                cancellationToken,
                configureServices: services =>
                    services.Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
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
                cancellationToken,
                configureServices: services =>
                    services.Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
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
                cancellationToken,
                configureServices: services =>
                    services.Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
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
            configureServices: services =>
                services
                    .AddSingleton<TimeProvider>(timeProvider)
                    .Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
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
                cancellationToken,
                configureServices: services =>
                    services.Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
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
                cancellationToken,
                configureServices: services =>
                    services.Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
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

                await Task.WhenAll(
                        outbox.MarkAsFailedAsync(pending[0].Id, "Scheduled error", TestDateTime.AddHours(1), token),
                        outbox.MarkAsFailedAsync(pending[1].Id, "Immediate error", token)
                    )
                    .ConfigureAwait(false);

                var failedForRetry = await outbox.GetFailedForRetryAsync(10, 50, token).ConfigureAwait(false);

                _ = await Assert.That(failedForRetry.Count).IsEqualTo(1);
            },
            cancellationToken,
            configureServices: services =>
                services
                    .AddSingleton<TimeProvider>(timeProvider)
                    .Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
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
            configureServices: services =>
                services
                    .AddSingleton<TimeProvider>(timeProvider)
                    .Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
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
                cancellationToken,
                configureServices: services =>
                    services.Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
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
            configureServices: services =>
                services
                    .AddSingleton<TimeProvider>(timeProvider)
                    .Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
        );
    }

    [Test]
    public async Task Should_GetDeadLetterMessages_Return_Empty_When_NoMessages(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var management = services.GetRequiredService<IOutboxManagement>();

                    var result = await management
                        .GetDeadLetterMessagesAsync(cancellationToken: token)
                        .ConfigureAwait(false);

                    _ = await Assert.That(result).IsEmpty();
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_GetDeadLetterMessages_Return_DeadLetterMessages(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();
                    await PublishEventsAsync(mediator, 3, x => new TestEvent { Id = $"Test{x:D3}" }, token);

                    var outbox = services.GetRequiredService<IOutboxRepository>();
                    var pending = await outbox.GetPendingAsync(50, token).ConfigureAwait(false);
                    await outbox
                        .MarkAsDeadLetterAsync([.. pending.Select(m => m.Id)], "Fatal error", token)
                        .ConfigureAwait(false);

                    var management = services.GetRequiredService<IOutboxManagement>();
                    var result = await management
                        .GetDeadLetterMessagesAsync(cancellationToken: token)
                        .ConfigureAwait(false);

                    _ = await Assert.That(result.Count).IsEqualTo(3);
                    _ = await Assert.That(result.All(m => m.Status == OutboxMessageStatus.DeadLetter)).IsTrue();
                },
                cancellationToken,
                configureServices: services =>
                    services.Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_GetDeadLetterMessages_Respect_PageSize(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();
                    await PublishEventsAsync(mediator, 5, x => new TestEvent { Id = $"Test{x:D3}" }, token);

                    var outbox = services.GetRequiredService<IOutboxRepository>();
                    var pending = await outbox.GetPendingAsync(50, token).ConfigureAwait(false);
                    await outbox
                        .MarkAsDeadLetterAsync([.. pending.Select(m => m.Id)], "Fatal error", token)
                        .ConfigureAwait(false);

                    var management = services.GetRequiredService<IOutboxManagement>();
                    var page0 = await management
                        .GetDeadLetterMessagesAsync(pageSize: 3, page: 0, cancellationToken: token)
                        .ConfigureAwait(false);
                    var page1 = await management
                        .GetDeadLetterMessagesAsync(pageSize: 3, page: 1, cancellationToken: token)
                        .ConfigureAwait(false);

                    using (Assert.Multiple())
                    {
                        _ = await Assert.That(page0.Count).IsEqualTo(3);
                        _ = await Assert.That(page1.Count).IsEqualTo(2);
                    }
                },
                cancellationToken,
                configureServices: services =>
                    services.Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_GetDeadLetterMessage_Return_Message_ById(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();
                    await mediator.PublishAsync(new TestEvent { Id = "Test001" }, token);

                    var outbox = services.GetRequiredService<IOutboxRepository>();
                    var pending = await outbox.GetPendingAsync(50, token).ConfigureAwait(false);
                    var messageId = pending[0].Id;
                    await outbox.MarkAsDeadLetterAsync(messageId, "Fatal error", token).ConfigureAwait(false);

                    var management = services.GetRequiredService<IOutboxManagement>();
                    var message = await management.GetDeadLetterMessageAsync(messageId, token).ConfigureAwait(false);

                    using (Assert.Multiple())
                    {
                        _ = await Assert.That(message).IsNotNull();
                        _ = await Assert.That(message!.Id).IsEqualTo(messageId);
                        _ = await Assert.That(message.Status).IsEqualTo(OutboxMessageStatus.DeadLetter);
                    }
                },
                cancellationToken,
                configureServices: services =>
                    services.Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_GetDeadLetterMessage_Return_Null_When_NotFound(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var management = services.GetRequiredService<IOutboxManagement>();

                    var message = await management
                        .GetDeadLetterMessageAsync(Guid.NewGuid(), token)
                        .ConfigureAwait(false);

                    _ = await Assert.That(message).IsNull();
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_GetDeadLetterCount_Return_Correct_Count(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();
                    await PublishEventsAsync(mediator, 3, x => new TestEvent { Id = $"Test{x:D3}" }, token);

                    var outbox = services.GetRequiredService<IOutboxRepository>();
                    var pending = await outbox.GetPendingAsync(50, token).ConfigureAwait(false);
                    await outbox
                        .MarkAsDeadLetterAsync([.. pending.Select(m => m.Id)], "Fatal error", token)
                        .ConfigureAwait(false);

                    var management = services.GetRequiredService<IOutboxManagement>();
                    var count = await management.GetDeadLetterCountAsync(token).ConfigureAwait(false);

                    _ = await Assert.That(count).IsEqualTo(3L);
                },
                cancellationToken,
                configureServices: services =>
                    services.Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_ReplayMessage_Reset_DeadLetter_To_Pending(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();
                    await mediator.PublishAsync(new TestEvent { Id = "Test001" }, token);

                    var outbox = services.GetRequiredService<IOutboxRepository>();
                    var pending = await outbox.GetPendingAsync(50, token).ConfigureAwait(false);
                    var messageId = pending[0].Id;
                    await outbox.MarkAsDeadLetterAsync(messageId, "Fatal error", token).ConfigureAwait(false);

                    var management = services.GetRequiredService<IOutboxManagement>();
                    var replayed = await management.ReplayMessageAsync(messageId, token).ConfigureAwait(false);

                    var pendingCount = await outbox.GetPendingCountAsync(token).ConfigureAwait(false);

                    using (Assert.Multiple())
                    {
                        _ = await Assert.That(replayed).IsTrue();
                        _ = await Assert.That(pendingCount).IsEqualTo(1L);
                    }
                },
                cancellationToken,
                configureServices: services =>
                    services.Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_ReplayMessage_Return_False_For_NonDeadLetter(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();
                    await mediator.PublishAsync(new TestEvent { Id = "Test001" }, token);

                    // GetPendingAsync moves the message to Processing — not a dead-letter
                    var outbox = services.GetRequiredService<IOutboxRepository>();
                    var pending = await outbox.GetPendingAsync(50, token).ConfigureAwait(false);

                    var management = services.GetRequiredService<IOutboxManagement>();
                    var replayed = await management.ReplayMessageAsync(pending[0].Id, token).ConfigureAwait(false);

                    _ = await Assert.That(replayed).IsFalse();
                },
                cancellationToken,
                configureServices: services =>
                    services.Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_ReplayAllDeadLetter_Reset_All_And_Return_Count(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();
                    await PublishEventsAsync(mediator, 3, x => new TestEvent { Id = $"Test{x:D3}" }, token);

                    var outbox = services.GetRequiredService<IOutboxRepository>();
                    var pending = await outbox.GetPendingAsync(50, token).ConfigureAwait(false);
                    await outbox
                        .MarkAsDeadLetterAsync([.. pending.Select(m => m.Id)], "Fatal error", token)
                        .ConfigureAwait(false);

                    var management = services.GetRequiredService<IOutboxManagement>();
                    var count = await management.ReplayAllDeadLetterAsync(token).ConfigureAwait(false);

                    var pendingCount = await outbox.GetPendingCountAsync(token).ConfigureAwait(false);

                    using (Assert.Multiple())
                    {
                        _ = await Assert.That(count).IsEqualTo(3);
                        _ = await Assert.That(pendingCount).IsEqualTo(3L);
                    }
                },
                cancellationToken,
                configureServices: services =>
                    services.Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_ReplayAllDeadLetter_Return_Zero_When_Empty(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var management = services.GetRequiredService<IOutboxManagement>();

                    var count = await management.ReplayAllDeadLetterAsync(token).ConfigureAwait(false);

                    _ = await Assert.That(count).IsEqualTo(0);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Should_GetStatistics_Return_Correct_Counts(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();
                    await PublishEventsAsync(mediator, 4, x => new TestEvent { Id = $"Test{x:D3}" }, token);

                    var outbox = services.GetRequiredService<IOutboxRepository>();

                    // Move msg0 → Completed
                    var batch1 = await outbox.GetPendingAsync(1, token).ConfigureAwait(false);
                    await outbox.MarkAsCompletedAsync(batch1[0].Id, token).ConfigureAwait(false);

                    // Move msg1 → DeadLetter
                    var batch2 = await outbox.GetPendingAsync(1, token).ConfigureAwait(false);
                    await outbox.MarkAsDeadLetterAsync(batch2[0].Id, "Fatal error", token).ConfigureAwait(false);

                    // msg2 and msg3 remain Pending
                    var management = services.GetRequiredService<IOutboxManagement>();
                    var statistics = await management.GetStatisticsAsync(token).ConfigureAwait(false);

                    using (Assert.Multiple())
                    {
                        _ = await Assert.That(statistics.Pending).IsEqualTo(2L);
                        _ = await Assert.That(statistics.Processing).IsEqualTo(0L);
                        _ = await Assert.That(statistics.Completed).IsEqualTo(1L);
                        _ = await Assert.That(statistics.Failed).IsEqualTo(0L);
                        _ = await Assert.That(statistics.DeadLetter).IsEqualTo(1L);
                        _ = await Assert.That(statistics.Total).IsEqualTo(4L);
                    }
                },
                cancellationToken,
                configureServices: services =>
                    services.Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
            )
            .ConfigureAwait(false);

    private sealed class TestEvent : IEvent
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }

        public required string Id { get; init; }

        public DateTimeOffset? PublishedAt { get; set; }
    }
}
