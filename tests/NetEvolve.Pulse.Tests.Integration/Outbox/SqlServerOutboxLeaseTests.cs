namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using NetEvolve.Pulse.Tests.Integration.Internals;

[ClassDataSource<SqlServerDatabaseServiceFixture, SqlServerAdoNetOutboxInitializer>(
    Shared = [SharedType.None, SharedType.None]
)]
[TestGroup("SqlServer")]
[TestGroup("AdoNet")]
[Timeout(300_000)] // SQL Server containers can take a long time to cold-start in CI environments.
public sealed class SqlServerOutboxLeaseTests(
    IServiceFixture databaseServiceFixture,
    IServiceInitializer databaseInitializer
) : PulseTestsBase(databaseServiceFixture, databaseInitializer)
{
    [Test]
    public async Task Should_GetPendingAsync_ReclaimExpiredProcessingLease(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var timeProvider = new FakeTimeProvider();
        timeProvider.AdjustTime(TestDateTime);

        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();
                    await mediator.PublishAsync(new TestEvent { Id = "Test001" }, token).ConfigureAwait(false);

                    var outbox = services.GetRequiredService<IOutboxRepository>();

                    // Claim the message, simulating a worker that picks it up for dispatch.
                    var claimed = await outbox.GetPendingAsync(10, token).ConfigureAwait(false);
                    _ = await Assert.That(claimed.Count).IsEqualTo(1);

                    // Simulate a crashed/cancelled worker: the message is never completed or failed,
                    // so it stays in Processing status. Advance past the default 5-minute lease.
                    timeProvider.Advance(TimeSpan.FromMinutes(10));

                    var reclaimed = await outbox.GetPendingAsync(10, token).ConfigureAwait(false);

                    _ = await Assert.That(reclaimed.Count).IsEqualTo(1);
                    _ = await Assert.That(reclaimed[0].Id).IsEqualTo(claimed[0].Id);
                },
                cancellationToken,
                configureServices: services =>
                    services
                        .AddSingleton<TimeProvider>(timeProvider)
                        .Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
            )
            .ConfigureAwait(false);
    }

    [Test]
    public async Task Should_GetPendingAsync_NotReclaim_WhileProcessingLeaseActive(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var timeProvider = new FakeTimeProvider();
        timeProvider.AdjustTime(TestDateTime);

        await RunAndVerify(
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();
                    await mediator.PublishAsync(new TestEvent { Id = "Test001" }, token).ConfigureAwait(false);

                    var outbox = services.GetRequiredService<IOutboxRepository>();

                    var claimed = await outbox.GetPendingAsync(10, token).ConfigureAwait(false);
                    _ = await Assert.That(claimed.Count).IsEqualTo(1);

                    // The lease (default 5 minutes) has not expired yet.
                    timeProvider.Advance(TimeSpan.FromMinutes(1));

                    var reclaimed = await outbox.GetPendingAsync(10, token).ConfigureAwait(false);

                    _ = await Assert.That(reclaimed).IsEmpty();
                },
                cancellationToken,
                configureServices: services =>
                    services
                        .AddSingleton<TimeProvider>(timeProvider)
                        .Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
            )
            .ConfigureAwait(false);
    }

    private sealed class TestEvent : IEvent
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }

        public required string Id { get; init; }

        public DateTimeOffset? PublishedAt { get; set; }
    }
}
