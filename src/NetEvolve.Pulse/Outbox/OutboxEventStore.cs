namespace NetEvolve.Pulse.Outbox;

using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Implementation of <see cref="IEventOutbox"/> that stores events using <see cref="IOutboxRepository"/>.
/// Serializes events using <see cref="IPayloadSerializer"/> and persists them for later processing by the background processor.
/// </summary>
/// <remarks>
/// <para><strong>Transaction Integration:</strong></para>
/// This implementation delegates to <see cref="IOutboxRepository.AddAsync"/> which SHOULD
/// participate in any ambient transaction to ensure atomicity with business operations.
/// <para><strong>Serialization:</strong></para>
/// Events are serialized using <see cref="IPayloadSerializer"/>. The assembly-qualified type name
/// is stored for deserialization by the message transport.
/// </remarks>
internal sealed class OutboxEventStore : IEventOutbox
{
    /// <summary>The repository used to persist outbox messages to the configured storage backend.</summary>
    private readonly IOutboxRepository _repository;

    /// <summary>The time provider used to generate consistent creation and update timestamps.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>The payload serializer used to serialize events to JSON.</summary>
    private readonly IPayloadSerializer _payloadSerializer;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxEventStore"/> class.
    /// </summary>
    /// <param name="repository">The repository for storing outbox messages.</param>
    /// <param name="timeProvider">The time provider for timestamps.</param>
    /// <param name="payloadSerializer">The payload serializer for serializing events.</param>
    public OutboxEventStore(
        IOutboxRepository repository,
        TimeProvider timeProvider,
        IPayloadSerializer payloadSerializer
    )
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(payloadSerializer);

        _repository = repository;
        _timeProvider = timeProvider;
        _payloadSerializer = payloadSerializer;
    }

    /// <inheritdoc />
    public Task StoreAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(message);

        var messageType = message.GetType();

        var correlationId = message.CorrelationId;

        if (correlationId is { Length: > OutboxMessageSchema.MaxLengths.CorrelationId })
        {
            throw new InvalidOperationException(
                $"CorrelationId exceeds the maximum length of {OutboxMessageSchema.MaxLengths.CorrelationId} characters defined by the OutboxMessage schema. "
                    + "Provide a shorter correlation identifier to comply with the database constraint."
            );
        }

        var causationId = message.CausationId;

        if (causationId is { Length: > OutboxMessageSchema.MaxLengths.CausationId })
        {
            throw new InvalidOperationException(
                $"CausationId exceeds the maximum length of {OutboxMessageSchema.MaxLengths.CausationId} characters defined by the OutboxMessage schema. "
                    + "Provide a shorter causation identifier to comply with the database constraint."
            );
        }

        var now = _timeProvider.GetUtcNow();

        var outboxMessage = new OutboxMessage
        {
            Id = message.ToOutboxId(),
            EventType = messageType,
            Payload = _payloadSerializer.Serialize(message, messageType),
            CorrelationId = correlationId,
            CausationId = causationId,
            CreatedAt = now,
            UpdatedAt = now,
            Status = OutboxMessageStatus.Pending,
        };

        return _repository.AddAsync(outboxMessage, cancellationToken);
    }
}
