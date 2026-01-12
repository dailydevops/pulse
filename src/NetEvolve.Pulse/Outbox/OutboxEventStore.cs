namespace NetEvolve.Pulse.Outbox;

using System.Text.Json;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Implementation of <see cref="IEventOutbox"/> that stores events using <see cref="IOutboxRepository"/>.
/// Serializes events to JSON and persists them for later processing by the background processor.
/// </summary>
/// <remarks>
/// <para><strong>Transaction Integration:</strong></para>
/// This implementation delegates to <see cref="IOutboxRepository.AddAsync"/> which SHOULD
/// participate in any ambient transaction to ensure atomicity with business operations.
/// <para><strong>Serialization:</strong></para>
/// Events are serialized to JSON using System.Text.Json. The assembly-qualified type name
/// is stored for deserialization by the message transport.
/// </remarks>
public sealed class OutboxEventStore : IEventOutbox
{
    private readonly IOutboxRepository _repository;
    private readonly OutboxOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxEventStore"/> class.
    /// </summary>
    /// <param name="repository">The repository for storing outbox messages.</param>
    /// <param name="options">The outbox options.</param>
    /// <param name="timeProvider">The time provider for timestamps.</param>
    public OutboxEventStore(IOutboxRepository repository, IOptions<OutboxOptions> options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _repository = repository;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task StoreAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(message);

        var now = _timeProvider.GetUtcNow();
        var messageType = message.GetType();
        var eventType =
            messageType.AssemblyQualifiedName
            ?? throw new InvalidOperationException($"Cannot get assembly-qualified name for type: {messageType}");

        if (eventType.Length > OutboxMessageSchema.MaxLengths.EventType)
        {
            throw new InvalidOperationException(
                $"Event type identifier exceeds the EventType column maximum length of {OutboxMessageSchema.MaxLengths.EventType} characters. "
                    + "Shorten the type identifier, increase the database column length, or use Type.FullName with a type registry."
            );
        }

        var correlationId = message.CorrelationId;

        if (correlationId is { Length: > OutboxMessageSchema.MaxLengths.CorrelationId })
        {
            throw new InvalidOperationException(
                $"CorrelationId exceeds the maximum length of {OutboxMessageSchema.MaxLengths.CorrelationId} characters defined by the OutboxMessage schema. "
                    + "Provide a shorter correlation identifier to comply with the database constraint."
            );
        }

        var outboxMessage = new OutboxMessage
        {
            Id = Guid.TryParse(message.Id, out var id) ? id : Guid.NewGuid(),
            EventType = eventType,
            Payload = JsonSerializer.Serialize(message, messageType, _options.JsonSerializerOptions),
            CorrelationId = correlationId,
            CreatedAt = now,
            UpdatedAt = now,
            Status = OutboxMessageStatus.Pending,
        };

        return _repository.AddAsync(outboxMessage, cancellationToken);
    }
}
