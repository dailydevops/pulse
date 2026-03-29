namespace NetEvolve.Pulse.Testing.Tests.Unit;

using NetEvolve.Pulse.Extensibility;
using TUnit.Core;

public class FakeMediatorTests
{
    [Test]
    public async Task SendAsync_WithConfiguredCommand_ReturnsSetupResponse()
    {
        var mediator = new FakeMediator();
        var expected = new TestCommandResponse("order-123");
        _ = mediator.SetupCommand<TestCommand, TestCommandResponse>().Returns(expected);

        var result = await mediator
            .SendAsync<TestCommand, TestCommandResponse>(new TestCommand("item-1"))
            .ConfigureAwait(false);

        _ = await Assert.That(result).IsNotNull();
        _ = await Assert.That(result.Id).IsEqualTo("order-123");
    }

    [Test]
    public async Task SendAsync_WithUnconfiguredCommand_ThrowsInvalidOperationException()
    {
        var mediator = new FakeMediator();

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await mediator.SendAsync<TestCommand, TestCommandResponse>(new TestCommand("item-1")).ConfigureAwait(false)
        );
    }

    [Test]
    public async Task SendAsync_WithSetupToThrow_ThrowsConfiguredException()
    {
        var mediator = new FakeMediator();
        _ = mediator.SetupCommand<TestCommand, TestCommandResponse>().Throws(new InvalidOperationException("fail"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await mediator.SendAsync<TestCommand, TestCommandResponse>(new TestCommand("item-1")).ConfigureAwait(false)
        );

        _ = await Assert.That(exception!.Message).IsEqualTo("fail");
    }

    [Test]
    public async Task SendAsync_VoidCommand_WithUnconfiguredCommand_ThrowsInvalidOperationException()
    {
        IMediator mediator = new FakeMediator();

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await mediator.SendAsync(new TestVoidCommand("delete-1")).ConfigureAwait(false)
        );
    }

    [Test]
    public async Task SendAsync_VoidCommand_WithConfiguredCommand_Succeeds()
    {
        var fakeMediator = new FakeMediator();
        _ = fakeMediator.SetupCommand<TestVoidCommand, Void>().Returns(Void.Completed);

        IMediator mediator = fakeMediator;
        await mediator.SendAsync(new TestVoidCommand("delete-1")).ConfigureAwait(false);

        fakeMediator.Verify<TestVoidCommand>(1);
    }

    [Test]
    public async Task QueryAsync_WithConfiguredQuery_ReturnsSetupResponse()
    {
        var mediator = new FakeMediator();
        var expected = new TestQueryResponse("John Doe");
        _ = mediator.SetupQuery<TestQuery, TestQueryResponse>().Returns(expected);

        var result = await mediator
            .QueryAsync<TestQuery, TestQueryResponse>(new TestQuery("user-1"))
            .ConfigureAwait(false);

        _ = await Assert.That(result).IsNotNull();
        _ = await Assert.That(result.Name).IsEqualTo("John Doe");
    }

    [Test]
    public async Task QueryAsync_WithUnconfiguredQuery_ThrowsInvalidOperationException()
    {
        var mediator = new FakeMediator();

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await mediator.QueryAsync<TestQuery, TestQueryResponse>(new TestQuery("user-1")).ConfigureAwait(false)
        );
    }

    [Test]
    public async Task QueryAsync_WithSetupToThrow_ThrowsConfiguredException()
    {
        var mediator = new FakeMediator();
        _ = mediator.SetupQuery<TestQuery, TestQueryResponse>().Throws(new InvalidOperationException("query fail"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await mediator.QueryAsync<TestQuery, TestQueryResponse>(new TestQuery("user-1")).ConfigureAwait(false)
        );

        _ = await Assert.That(exception!.Message).IsEqualTo("query fail");
    }

    [Test]
    public async Task PublishAsync_WithConfiguredEvent_CapturesEvent()
    {
        var mediator = new FakeMediator();
        _ = mediator.SetupEvent<TestEvent>();

        var testEvent = new TestEvent { CorrelationId = "corr-1" };
        await mediator.PublishAsync(testEvent).ConfigureAwait(false);

        var events = mediator.GetPublishedEvents<TestEvent>();
        _ = await Assert.That(events).Count().IsEqualTo(1);
        _ = await Assert.That(events[0].CorrelationId).IsEqualTo("corr-1");
    }

    [Test]
    public async Task PublishAsync_WithUnconfiguredEvent_DoesNotThrow()
    {
        var mediator = new FakeMediator();
        var testEvent = new TestEvent();

        await mediator.PublishAsync(testEvent).ConfigureAwait(false);
    }

    [Test]
    public async Task PublishAsync_SetsPublishedAtOnEvent()
    {
        var mediator = new FakeMediator();
        var testEvent = new TestEvent();
        var before = DateTimeOffset.UtcNow;

        await mediator.PublishAsync(testEvent).ConfigureAwait(false);

        _ = await Assert.That(testEvent.PublishedAt).IsNotNull();
        _ = await Assert.That(testEvent.PublishedAt!.Value).IsGreaterThanOrEqualTo(before);
    }

    [Test]
    public async Task GetPublishedEvents_ReturnsEventsInOrder()
    {
        var mediator = new FakeMediator();
        _ = mediator.SetupEvent<TestEvent>();

        var event1 = new TestEvent { CorrelationId = "first" };
        var event2 = new TestEvent { CorrelationId = "second" };
        var event3 = new TestEvent { CorrelationId = "third" };

        await mediator.PublishAsync(event1).ConfigureAwait(false);
        await mediator.PublishAsync(event2).ConfigureAwait(false);
        await mediator.PublishAsync(event3).ConfigureAwait(false);

        var events = mediator.GetPublishedEvents<TestEvent>();

        using (Assert.Multiple())
        {
            _ = await Assert.That(events).Count().IsEqualTo(3);
            _ = await Assert.That(events[0].CorrelationId).IsEqualTo("first");
            _ = await Assert.That(events[1].CorrelationId).IsEqualTo("second");
            _ = await Assert.That(events[2].CorrelationId).IsEqualTo("third");
        }
    }

    [Test]
    public async Task GetPublishedEvents_WithNoPublishedEvents_ReturnsEmpty()
    {
        var mediator = new FakeMediator();

        var events = mediator.GetPublishedEvents<TestEvent>();

        _ = await Assert.That(events).IsEmpty();
    }

    [Test]
    public async Task Verify_WithMatchingCount_DoesNotThrow()
    {
        var mediator = new FakeMediator();
        _ = mediator.SetupEvent<TestEvent>();

        await mediator.PublishAsync(new TestEvent()).ConfigureAwait(false);
        await mediator.PublishAsync(new TestEvent()).ConfigureAwait(false);

        mediator.Verify<TestEvent>(2);
    }

    [Test]
    public async Task Verify_WithMismatchedCount_ThrowsInvalidOperationException()
    {
        var mediator = new FakeMediator();
        _ = mediator.SetupCommand<TestCommand, TestCommandResponse>().Returns(new TestCommandResponse("ok"));

        _ = await mediator.SendAsync<TestCommand, TestCommandResponse>(new TestCommand("item-1")).ConfigureAwait(false);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            mediator.Verify<TestCommand>(2);
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task Verify_WithNoInvocations_ThrowsInvalidOperationException()
    {
        var mediator = new FakeMediator();

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            mediator.Verify<TestCommand>(1);
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task SetupCommand_Fluent_ReturnsMediator()
    {
        var mediator = new FakeMediator();

        var result = mediator.SetupCommand<TestCommand, TestCommandResponse>().Returns(new TestCommandResponse("ok"));

        _ = await Assert.That(result).IsSameReferenceAs(mediator);
    }

    [Test]
    public async Task SetupQuery_Fluent_ReturnsMediator()
    {
        var mediator = new FakeMediator();

        var result = mediator.SetupQuery<TestQuery, TestQueryResponse>().Returns(new TestQueryResponse("ok"));

        _ = await Assert.That(result).IsSameReferenceAs(mediator);
    }

    [Test]
    public async Task SetupEvent_Fluent_ReturnsMediator()
    {
        var mediator = new FakeMediator();

        var result = mediator.SetupEvent<TestEvent>();

        _ = await Assert.That(result).IsSameReferenceAs(mediator);
    }

    [Test]
    public async Task SendAsync_WithSetupToThrowGeneric_ThrowsConfiguredException()
    {
        var mediator = new FakeMediator();
        _ = mediator.SetupCommand<TestCommand, TestCommandResponse>().Throws<InvalidOperationException>();

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await mediator.SendAsync<TestCommand, TestCommandResponse>(new TestCommand("item-1")).ConfigureAwait(false)
        );
    }

    [Test]
    public async Task QueryAsync_WithSetupToThrowGeneric_ThrowsConfiguredException()
    {
        var mediator = new FakeMediator();
        _ = mediator.SetupQuery<TestQuery, TestQueryResponse>().Throws<InvalidOperationException>();

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await mediator.QueryAsync<TestQuery, TestQueryResponse>(new TestQuery("user-1")).ConfigureAwait(false)
        );
    }

    private sealed record TestCommand(string ItemId) : ICommand<TestCommandResponse>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record TestCommandResponse(string Id);

    private sealed record TestVoidCommand(string ItemId) : ICommand
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record TestQuery(string UserId) : IQuery<TestQueryResponse>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record TestQueryResponse(string Name);

    private sealed class TestEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
