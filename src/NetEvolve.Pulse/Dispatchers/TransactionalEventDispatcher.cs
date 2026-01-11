namespace NetEvolve.Pulse.Dispatchers;

using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Event dispatcher that stores events in an outbox for reliable, transactional processing.
/// Implements the outbox pattern for guaranteed event delivery with at-least-once semantics.
/// </summary>
/// <remarks>
/// <para><strong>Transactional Guarantee:</strong></para>
/// Events are stored in the outbox within the same transaction as business operations,
/// ensuring consistency between state changes and event publishing.
/// <para><strong>Processing Model:</strong></para>
/// <list type="number">
/// <item><description>Event is stored in outbox (this dispatcher)</description></item>
/// <item><description>Transaction commits with business data and event</description></item>
/// <item><description>Separate background processor reads and dispatches events</description></item>
/// <item><description>Handlers execute with at-least-once delivery guarantee</description></item>
/// </list>
/// <para><strong>Handler Invocation:</strong></para>
/// This dispatcher does NOT invoke handlers directly. It only stores events for later processing.
/// A separate <see cref="IEventOutbox"/> processor is responsible for actual handler invocation.
/// <para><strong>Error Handling:</strong></para>
/// If outbox storage fails, the exception propagates and should cause the transaction to roll back.
/// This ensures no events are lost.
/// <para><strong>Use Cases:</strong></para>
/// <list type="bullet">
/// <item><description>Microservices requiring reliable inter-service communication</description></item>
/// <item><description>Event sourcing with guaranteed event persistence</description></item>
/// <item><description>Integration with external systems requiring delivery guarantees</description></item>
/// <item><description>Saga/process manager patterns</description></item>
/// </list>
/// <para><strong>⚠️ Requirements:</strong></para>
/// <list type="bullet">
/// <item><description>An <see cref="IEventOutbox"/> implementation must be registered in DI</description></item>
/// <item><description>A background processor must be configured to process outbox entries</description></item>
/// <item><description>Event handlers MUST be idempotent (at-least-once delivery)</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // Register transactional dispatcher
/// services.AddPulse(config =&gt;
/// {
///     config.UseDefaultEventDispatcher&lt;TransactionalEventDispatcher&gt;();
/// });
///
/// // Register outbox implementation
/// services.AddScoped&lt;IEventOutbox, SqlServerEventOutbox&gt;();
///
/// // Configure background processor (e.g., with hosted service)
/// services.AddHostedService&lt;OutboxProcessor&gt;();
/// </code>
/// </example>
/// <seealso cref="IEventDispatcher"/>
/// <seealso cref="IEventOutbox"/>
public sealed class TransactionalEventDispatcher : IEventDispatcher
{
    /// <summary>
    /// The outbox for storing events for later processing.
    /// </summary>
    private readonly IEventOutbox _outbox;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransactionalEventDispatcher"/> class.
    /// </summary>
    /// <param name="outbox">The outbox for storing events.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="outbox"/> is null.</exception>
    public TransactionalEventDispatcher(IEventOutbox outbox)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        _outbox = outbox;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><strong>Important:</strong></para>
    /// This implementation does NOT invoke handlers directly. It only stores the event in the outbox.
    /// The <paramref name="handlers"/> and <paramref name="invoker"/> parameters are not used.
    /// <para><strong>Transaction Integration:</strong></para>
    /// Call this within a transaction scope to ensure the event is persisted atomically
    /// with related business data.
    /// </remarks>
    public Task DispatchAsync<TEvent>(
        TEvent message,
        IEnumerable<IEventHandler<TEvent>> handlers,
        Func<IEventHandler<TEvent>, TEvent, Task> invoker,
        CancellationToken cancellationToken
    )
        where TEvent : IEvent =>
        // Store in outbox for later processing - handlers are invoked by the outbox processor
        _outbox.StoreAsync(message, cancellationToken);
}
