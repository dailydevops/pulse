namespace NetEvolve.Pulse.Outbox;

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;

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
internal sealed class InMemoryMessageTransport : IMessageTransport
{
    /// <summary>Cache of compiled publish delegates keyed by concrete event type, avoiding per-message reflection overhead.</summary>
    private static readonly ConcurrentDictionary<
        Type,
        Func<IMediator, IEvent, CancellationToken, Task>
    > PublishDelegates = new();

    /// <summary>The mediator used to publish deserialized events in-process.</summary>
    private readonly IMediator _mediator;

    /// <summary>The resolved outbox options, providing JSON serialization settings for event deserialization.</summary>
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

        var eventType = message.EventType;

        var @event =
            JsonSerializer.Deserialize(message.Payload, eventType, _options.JsonSerializerOptions)
            ?? throw new InvalidOperationException(
                $"Failed to deserialize event payload for type: {eventType}"
            );

        if (@event is not IEvent typedEvent)
        {
            throw new InvalidOperationException($"Deserialized object is not an IEvent: {eventType}");
        }

        await PublishEventAsync(typedEvent, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves a compiled delegate for the concrete event type (cached after first call) and invokes
    /// <see cref="IMediator.PublishAsync{TEvent}"/>, eliminating per-message reflection overhead.
    /// </summary>
    /// <param name="event">The deserialized event to publish through the mediator.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous publish operation.</returns>
    private Task PublishEventAsync(IEvent @event, CancellationToken cancellationToken) =>
        PublishDelegates.GetOrAdd(@event.GetType(), CreatePublishDelegate)(_mediator, @event, cancellationToken);

    /// <summary>
    /// Builds a compiled expression-tree delegate that calls <see cref="IMediator.PublishAsync{TEvent}"/>
    /// with the given concrete <paramref name="eventType"/>. Called once per event type and cached.
    /// </summary>
    /// <param name="eventType">The concrete event type for which to create a publish delegate.</param>
    /// <returns>A compiled delegate that can publish events of the specified type through the mediator.</returns>
    private static Func<IMediator, IEvent, CancellationToken, Task> CreatePublishDelegate(Type eventType)
    {
        var mediatorParam = Expression.Parameter(typeof(IMediator), "mediator");
        var eventParam = Expression.Parameter(typeof(IEvent), "event");
        var tokenParam = Expression.Parameter(typeof(CancellationToken), "cancellationToken");

        var method = typeof(IMediator).GetMethod(nameof(IMediator.PublishAsync))!.MakeGenericMethod(eventType);
        var call = Expression.Call(mediatorParam, method, Expression.Convert(eventParam, eventType), tokenParam);

        return Expression
            .Lambda<Func<IMediator, IEvent, CancellationToken, Task>>(call, mediatorParam, eventParam, tokenParam)
            .Compile();
    }
}
