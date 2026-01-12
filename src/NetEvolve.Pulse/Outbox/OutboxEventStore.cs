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

        var outboxMessage = new OutboxMessage
        {
            Id = Guid.TryParse(message.Id, out var id) ? id : Guid.NewGuid(),
            EventType =
                message.GetType().AssemblyQualifiedName
                ?? throw new InvalidOperationException(
                    $"Cannot get assembly-qualified name for type: {message.GetType()}"
                ),
            Payload = JsonSerializer.Serialize(message, message.GetType(), _options.JsonSerializerOptions),
            CorrelationId = message.CorrelationId,
            CreatedAt = now,
            UpdatedAt = now,
            Status = OutboxMessageStatus.Pending,
        };

        return _repository.AddAsync(outboxMessage, cancellationToken);
    }
}
