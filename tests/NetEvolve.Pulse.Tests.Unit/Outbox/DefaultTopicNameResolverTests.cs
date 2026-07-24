namespace NetEvolve.Pulse.Tests.Unit.Outbox;

using System;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// Unit tests for <see cref="DefaultTopicNameResolver"/>.
/// Verifies that the default resolver returns the simple class name of the event type
/// and guards against a null message argument.
/// </summary>
[TestGroup("Outbox")]
public sealed class DefaultTopicNameResolverTests
{
    [Test]
    public async Task Resolve_WithSimpleType_ReturnsSimpleClassName()
    {
        var resolver = new DefaultTopicNameResolver();
        var message = CreateMessage(typeof(OrderCreated));

        var topic = resolver.Resolve(message);

        _ = await Assert.That(topic).IsEqualTo(nameof(OrderCreated));
    }

    [Test]
    public async Task Resolve_WithNestedType_ReturnsNestedSimpleClassName()
    {
        var resolver = new DefaultTopicNameResolver();
        var message = CreateMessage(typeof(NestedEvent));

        var topic = resolver.Resolve(message);

        _ = await Assert.That(topic).IsEqualTo(nameof(NestedEvent));
    }

    [Test]
    public async Task Resolve_WithGenericType_ReturnsBacktickNameWithoutTypeArguments()
    {
        var resolver = new DefaultTopicNameResolver();
        var message = CreateMessage(typeof(GenericEvent<string>));

        var topic = resolver.Resolve(message);

        _ = await Assert.That(topic).IsEqualTo("GenericEvent`1");
    }

    [Test]
    public async Task Resolve_WithBuiltInType_ReturnsSimpleClassName()
    {
        var resolver = new DefaultTopicNameResolver();
        var message = CreateMessage(typeof(object));

        var topic = resolver.Resolve(message);

        _ = await Assert.That(topic).IsEqualTo(nameof(Object));
    }

    [Test]
    public async Task Resolve_WithNullMessage_ThrowsArgumentNullException()
    {
        var resolver = new DefaultTopicNameResolver();

        _ = await Assert.That(() => resolver.Resolve(null!)).Throws<ArgumentNullException>();
    }

    private static OutboxMessage CreateMessage(Type eventType) =>
        new()
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = OutboxMessageStatus.Processing,
        };

    private sealed record OrderCreated : IEvent
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed record NestedEvent : IEvent
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed record GenericEvent<T>(T? Value) : IEvent
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
