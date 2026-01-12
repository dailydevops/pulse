namespace NetEvolve.Pulse.Outbox;

using System.Text.Json;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Default message transport that dispatches outbox messages through the mediator.
/// Deserializes events and publishes them in-process for handler execution.
/// </summary>
/// <remarks>
/// <para><strong>Use Case:</strong></para>
/// Use this transport when events should be handled in the same process that stored them.
/// The outbox pattern still provides reliability by persisting events before processing.
/// <para><strong>Serialization:</strong></para>
/// Events are deserialized using System.Text.Json with optional custom settings from <see cref="OutboxOptions"/>.
/// The <see cref="OutboxMessage.EventType"/> property contains the assembly-qualified type name.
/// <para><strong>Error Handling:</strong></para>
/// Exceptions from event handlers propagate to the outbox processor for retry handling.
/// </remarks>
public sealed class InMemoryMessageTransport : IMessageTransport
{
    private readonly IMediator _mediator;
    private readonly OutboxOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryMessageTransport"/> class.
    /// </summary>
    /// <param name="mediator">The mediator for publishing events.</param>
    /// <param name="options">The outbox options containing serialization settings.</param>
    public InMemoryMessageTransport(IMediator mediator, IOptions<OutboxOptions> options)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        ArgumentNullException.ThrowIfNull(options);

        _mediator = mediator;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var eventType =
            Type.GetType(message.EventType)
            ?? throw new InvalidOperationException($"Cannot resolve event type: {message.EventType}");

        var @event =
            JsonSerializer.Deserialize(message.Payload, eventType, _options.JsonSerializerOptions)
            ?? throw new InvalidOperationException(
                $"Failed to deserialize event payload for type: {message.EventType}"
            );

        if (@event is not IEvent typedEvent)
        {
            throw new InvalidOperationException($"Deserialized object is not an IEvent: {message.EventType}");
        }

        await PublishEventAsync(typedEvent, cancellationToken).ConfigureAwait(false);
    }

    private Task PublishEventAsync(IEvent @event, CancellationToken cancellationToken)
    {
        // Use reflection to call PublishAsync<TEvent> with the concrete type
        var publishMethod = typeof(IMediator)
            .GetMethod(nameof(IMediator.PublishAsync))!
            .MakeGenericMethod(@event.GetType());

        return (Task)publishMethod.Invoke(_mediator, [@event, cancellationToken])!;
    }
}
