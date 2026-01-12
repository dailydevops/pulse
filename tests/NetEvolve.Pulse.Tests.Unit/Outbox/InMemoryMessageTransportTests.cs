namespace NetEvolve.Pulse.Tests.Unit.Outbox;

using System.Text.Json;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

/// <summary>
/// Unit tests for <see cref="InMemoryMessageTransport"/>.
/// Tests constructor validation, event deserialization, and mediator dispatch.
/// </summary>
public sealed class InMemoryMessageTransportTests
{
    #region Constructor Tests

    [Test]
    public async Task Constructor_WithNullMediator_ThrowsArgumentNullException()
    {
        IMediator? mediator = null;
        var options = Options.Create(new OutboxOptions());

        _ = Assert.Throws<ArgumentNullException>(
            "mediator",
            () => _ = new InMemoryMessageTransport(mediator!, options)
        );
    }

    [Test]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        var mediator = new TestMediator();
        IOptions<OutboxOptions>? options = null;

        _ = Assert.Throws<ArgumentNullException>("options", () => _ = new InMemoryMessageTransport(mediator, options!));
    }

    [Test]
    public async Task Constructor_WithValidParameters_CreatesInstance()
    {
        var mediator = new TestMediator();
        var options = Options.Create(new OutboxOptions());

        var transport = new InMemoryMessageTransport(mediator, options);

        _ = await Assert.That(transport).IsNotNull();
    }

    #endregion

    #region SendAsync Tests

    [Test]
    public async Task SendAsync_WithNullMessage_ThrowsArgumentNullException()
    {
        var mediator = new TestMediator();
        var options = Options.Create(new OutboxOptions());
        var transport = new InMemoryMessageTransport(mediator, options);

        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => transport.SendAsync(null!));
    }

    [Test]
    public async Task SendAsync_WithValidMessage_DeserializesAndPublishesEvent()
    {
        var mediator = new TestMediator();
        var options = Options.Create(new OutboxOptions());
        var transport = new InMemoryMessageTransport(mediator, options);

        var originalEvent = new TestTransportEvent("test-id", "test data");
        var message = CreateOutboxMessage(originalEvent);

        await transport.SendAsync(message).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(mediator.PublishedEvents).HasSingleItem();
            _ = await Assert.That(mediator.PublishedEvents[0]).IsTypeOf<TestTransportEvent>();

            var publishedEvent = (TestTransportEvent)mediator.PublishedEvents[0];
            _ = await Assert.That(publishedEvent.Id).IsEqualTo(originalEvent.Id);
            _ = await Assert.That(publishedEvent.Data).IsEqualTo(originalEvent.Data);
        }
    }

    [Test]
    public async Task SendAsync_WithUnresolvableEventType_ThrowsInvalidOperationException()
    {
        var mediator = new TestMediator();
        var options = Options.Create(new OutboxOptions());
        var transport = new InMemoryMessageTransport(mediator, options);

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = "NonExistent.Type, NonExistent.Assembly",
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = OutboxMessageStatus.Processing,
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => transport.SendAsync(message));

        _ = await Assert.That(exception!.Message).Contains("Cannot resolve event type");
    }

    [Test]
    public async Task SendAsync_WithInvalidPayload_ThrowsInvalidOperationException()
    {
        var mediator = new TestMediator();
        var options = Options.Create(new OutboxOptions());
        var transport = new InMemoryMessageTransport(mediator, options);

        var eventType = typeof(TestTransportEvent).AssemblyQualifiedName!;
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            Payload = "null", // This will deserialize to null
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = OutboxMessageStatus.Processing,
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => transport.SendAsync(message));

        _ = await Assert.That(exception!.Message).Contains("Failed to deserialize");
    }

    [Test]
    public async Task SendAsync_WithNonEventType_ThrowsInvalidOperationException()
    {
        var mediator = new TestMediator();
        var options = Options.Create(new OutboxOptions());
        var transport = new InMemoryMessageTransport(mediator, options);

        // Use a type that is not an IEvent
        var eventType = typeof(NonEventClass).AssemblyQualifiedName!;
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            Payload = """{"Value":"test"}""",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = OutboxMessageStatus.Processing,
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => transport.SendAsync(message));

        _ = await Assert.That(exception!.Message).Contains("is not an IEvent");
    }

    [Test]
    public async Task SendAsync_WithCustomJsonOptions_UsesProvidedOptions()
    {
        var mediator = new TestMediator();
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var options = Options.Create(new OutboxOptions { JsonSerializerOptions = jsonOptions });
        var transport = new InMemoryMessageTransport(mediator, options);

        // Create payload with camelCase property names
        const string payload = """{"id":"custom-id","data":"custom data","correlationId":null,"publishedAt":null}""";
        var eventType = typeof(TestTransportEvent).AssemblyQualifiedName!;
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            Payload = payload,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = OutboxMessageStatus.Processing,
        };

        await transport.SendAsync(message).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(mediator.PublishedEvents).HasSingleItem();
            var publishedEvent = (TestTransportEvent)mediator.PublishedEvents[0];
            _ = await Assert.That(publishedEvent.Id).IsEqualTo("custom-id");
            _ = await Assert.That(publishedEvent.Data).IsEqualTo("custom data");
        }
    }

    [Test]
    public async Task SendAsync_WithCancellationToken_PropagatesToken()
    {
        var mediator = new TestMediator();
        var options = Options.Create(new OutboxOptions());
        var transport = new InMemoryMessageTransport(mediator, options);

        using var cts = new CancellationTokenSource();
        var originalEvent = new TestTransportEvent("test-id", "test data");
        var message = CreateOutboxMessage(originalEvent);

        await transport.SendAsync(message, cts.Token).ConfigureAwait(false);

        _ = await Assert.That(mediator.LastCancellationToken).IsEqualTo(cts.Token);
    }

    [Test]
    public async Task SendAsync_WhenMediatorThrows_PropagatesException()
    {
        var mediator = new ThrowingMediator(new InvalidOperationException("Mediator error"));
        var options = Options.Create(new OutboxOptions());
        var transport = new InMemoryMessageTransport(mediator, options);

        var originalEvent = new TestTransportEvent("test-id", "test data");
        var message = CreateOutboxMessage(originalEvent);

        // Reflection-based invocation wraps exceptions in TargetInvocationException
        var exception = await Assert.ThrowsAsync<System.Reflection.TargetInvocationException>(() =>
            transport.SendAsync(message)
        );

        using (Assert.Multiple())
        {
            _ = await Assert.That(exception!.InnerException).IsNotNull();
            _ = await Assert.That(exception.InnerException).IsTypeOf<InvalidOperationException>();
            _ = await Assert.That(exception.InnerException!.Message).IsEqualTo("Mediator error");
        }
    }

    #endregion

    #region Helper Methods

    private static OutboxMessage CreateOutboxMessage(TestTransportEvent @event)
    {
        var eventType = @event.GetType().AssemblyQualifiedName!;
        var payload = JsonSerializer.Serialize(@event, @event.GetType());

        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            Payload = payload,
            CorrelationId = @event.CorrelationId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = OutboxMessageStatus.Processing,
        };
    }

    #endregion

    #region Test Doubles

#pragma warning disable CS8767 // Nullability of reference types in type of parameter doesn't match
    private sealed class TestMediator : IMediator
    {
        public List<IEvent> PublishedEvents { get; } = [];
        public CancellationToken LastCancellationToken { get; private set; }

        public Task PublishAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default)
            where TEvent : notnull, IEvent
        {
            PublishedEvents.Add(message);
            LastCancellationToken = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<TResponse> QueryAsync<TQuery, TResponse>(
            TQuery query,
            CancellationToken cancellationToken = default
        )
            where TQuery : notnull, IQuery<TResponse> => throw new NotSupportedException();

        public Task<TResponse> SendAsync<TCommand, TResponse>(
            TCommand command,
            CancellationToken cancellationToken = default
        )
            where TCommand : notnull, ICommand<TResponse> => throw new NotSupportedException();

        public Task SendAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
            where TCommand : notnull, ICommand => throw new NotSupportedException();
    }

    private sealed class ThrowingMediator : IMediator
    {
        private readonly Exception _exception;

        public ThrowingMediator(Exception exception) => _exception = exception;

        public Task PublishAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default)
            where TEvent : notnull, IEvent => throw _exception;

        public Task<TResponse> QueryAsync<TQuery, TResponse>(
            TQuery query,
            CancellationToken cancellationToken = default
        )
            where TQuery : notnull, IQuery<TResponse> => throw new NotSupportedException();

        public Task<TResponse> SendAsync<TCommand, TResponse>(
            TCommand command,
            CancellationToken cancellationToken = default
        )
            where TCommand : notnull, ICommand<TResponse> => throw new NotSupportedException();

        public Task SendAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
            where TCommand : notnull, ICommand => throw new NotSupportedException();
    }
#pragma warning restore CS8767

    private sealed class TestTransportEvent : IEvent
    {
        public TestTransportEvent() { }

        public TestTransportEvent(string id, string data)
        {
            Id = id;
            Data = data;
        }

        public string Id { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
        public string? CorrelationId { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class NonEventClass
    {
        public string Value { get; set; } = string.Empty;
    }

    #endregion
}
