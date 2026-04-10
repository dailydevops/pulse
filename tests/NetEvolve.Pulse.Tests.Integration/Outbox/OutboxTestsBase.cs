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

                    var publishTasks = PublishEvents(mediator, 3, x => new TestEvent { Id = $"Test{x:D3}" });
                    await Task.WhenAll(publishTasks);

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

                var publishTasks = PublishEvents(mediator, 3, x => new TestEvent { Id = $"Test{x:D3}" });
                await Task.WhenAll(publishTasks);

                var outbox = services.GetRequiredService<IOutboxRepository>();
                var result = await outbox.GetPendingAsync(50, token).ConfigureAwait(false);

                _ = await Verify(result.OrderBy(x => x.Payload)).HashParameters().ConfigureAwait(false);
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
