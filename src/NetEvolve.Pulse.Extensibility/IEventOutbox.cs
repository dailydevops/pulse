namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Defines the contract for an outbox that stores events for reliable delivery.
/// Used by transactional dispatchers to implement the outbox pattern for guaranteed event processing.
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong></para>
/// The outbox pattern ensures events are persisted atomically with business data,
/// then processed asynchronously for reliable delivery even after system failures.
/// <para><strong>Implementation Guidelines:</strong></para>
/// <list type="bullet">
/// <item><description><see cref="StoreAsync{TEvent}"/> MUST be called within the same transaction as business operations</description></item>
/// <item><description>Implementations SHOULD support multiple storage backends (SQL, CosmosDB, etc.)</description></item>
/// <item><description>Events SHOULD include correlation IDs for distributed tracing</description></item>
/// </list>
/// <para><strong>Processing Flow:</strong></para>
/// <list type="number">
/// <item><description>Business operation and event storage in same transaction</description></item>
/// <item><description>Transaction commits</description></item>
/// <item><description>Background processor reads from outbox and dispatches to handlers</description></item>
/// <item><description>Successfully processed events are marked complete or deleted</description></item>
/// </list>
/// <para><strong>⚠️ Important:</strong></para>
/// Event handlers MUST be idempotent since events may be delivered more than once
/// (at-least-once delivery semantics).
/// </remarks>
/// <example>
/// <code>
/// // SQL Server implementation example
/// public class SqlOutbox : IEventOutbox
/// {
///     private readonly DbContext _context;
///
///     public async Task StoreAsync&lt;TEvent&gt;(TEvent message, CancellationToken ct)
///         where TEvent : IEvent
///     {
///         var entry = new OutboxEntry
///         {
///             Id = message.Id,
///             EventType = typeof(TEvent).AssemblyQualifiedName,
///             Payload = JsonSerializer.Serialize(message),
///             CreatedAt = DateTimeOffset.UtcNow
///         };
///         _context.OutboxEntries.Add(entry);
///         await _context.SaveChangesAsync(ct);
///     }
/// }
/// </code>
/// </example>
/// <seealso cref="IEvent"/>
/// <seealso cref="IEventDispatcher"/>
public interface IEventOutbox
{
    /// <summary>
    /// Stores an event in the outbox for later processing.
    /// </summary>
    /// <typeparam name="TEvent">The type of event to store.</typeparam>
    /// <param name="message">The event to store in the outbox.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous store operation.</returns>
    /// <remarks>
    /// <para><strong>Transaction Scope:</strong></para>
    /// This method SHOULD be called within an ambient transaction (e.g., <see cref="System.Transactions.TransactionScope"/>)
    /// to ensure atomicity with business operations.
    /// <para><strong>Serialization:</strong></para>
    /// Events SHOULD be serialized to a format that supports schema evolution (JSON recommended).
    /// Include the full type name for deserialization.
    /// </remarks>
    Task StoreAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default)
        where TEvent : IEvent;
}
