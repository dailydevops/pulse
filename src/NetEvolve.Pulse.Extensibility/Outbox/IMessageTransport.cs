namespace NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Defines the contract for transporting outbox messages to external systems or handlers.
/// Implementations enable pluggable delivery strategies for message brokers (Kafka, RabbitMQ, etc.)
/// or in-process handling via the mediator.
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong></para>
/// The message transport abstraction decouples outbox processing from the delivery mechanism,
/// enabling different deployment scenarios:
/// <list type="bullet">
/// <item><description>In-process: Dispatch directly to <see cref="IMediatorSendOnly.PublishAsync{TEvent}"/></description></item>
/// <item><description>Message broker: Send to Kafka, RabbitMQ, Azure Service Bus, etc.</description></item>
/// <item><description>HTTP: Forward to webhook endpoints</description></item>
/// </list>
/// <para><strong>Delivery Guarantees:</strong></para>
/// Implementations SHOULD provide at-least-once delivery semantics.
/// Handlers MUST be idempotent to handle potential duplicate deliveries.
/// <para><strong>Error Handling:</strong></para>
/// <list type="bullet">
/// <item><description>Throw exceptions for transient failures (network issues, broker unavailable)</description></item>
/// <item><description>The outbox processor will handle retries based on processor options</description></item>
/// </list>
/// </remarks>
public interface IMessageTransport
{
    /// <summary>
    /// Sends a single outbox message to the destination.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous send operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="message"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the message cannot be processed.</exception>
    Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends multiple outbox messages in a batch for improved performance.
    /// </summary>
    /// <param name="messages">The messages to send.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous batch send operation.</returns>
    /// <remarks>
    /// <para><strong>Default Implementation (best-effort, non-atomic):</strong></para>
    /// The default implementation calls <see cref="SendAsync"/> for each message sequentially and may
    /// deliver a subset if an exception occurs mid-batch. Override when the transport supports atomic or
    /// batched publishing to avoid partial delivery and improve throughput.
    /// <para><strong>Atomicity Guidance for Implementers:</strong></para>
    /// When the transport can guarantee atomic batches, treat the operation as all-or-nothing. If partial
    /// delivery is possible, implement compensation or ensure idempotent handlers, because the outbox
    /// processor will mark all messages as failed and retry the batch on the next poll.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="messages"/> is null.</exception>
    Task SendBatchAsync(IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        return SendBatchInternalAsync(messages, cancellationToken);
    }

    /// <summary>
    /// Internal implementation of batch sending that dispatches each message concurrently via <see cref="SendAsync"/>.
    /// Uses <see cref="Parallel.ForEachAsync{TSource}(IEnumerable{TSource}, CancellationToken, Func{TSource, CancellationToken, ValueTask})"/> to maximize throughput while respecting the provided cancellation token.
    /// </summary>
    /// <remarks>
    /// <para><strong>Concurrency Model:</strong></para>
    /// Messages are processed in parallel (not sequentially), which may improve throughput significantly.
    /// The degree of parallelism is determined by the system's thread pool.
    /// <para><strong>Error Handling:</strong></para>
    /// Individual message send failures propagate as exceptions and interrupt batch processing.
    /// The first exception encountered will be thrown, terminating the batch operation.
    /// <para><strong>Cancellation:</strong></para>
    /// If the cancellation token is triggered, all remaining concurrent operations are cancelled,
    /// and the task completes. Already-attempted messages may have partial results depending on
    /// the transport's concurrency model.
    /// </remarks>
    /// <param name="messages">The collection of messages to send concurrently.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the concurrent send operation that completes when all messages have been attempted.</returns>
    private async Task SendBatchInternalAsync(
        IEnumerable<OutboxMessage> messages,
        CancellationToken cancellationToken
    ) =>
        await Parallel
            .ForEachAsync(
                messages,
                cancellationToken,
                async (message, token) => await SendAsync(message, token).ConfigureAwait(false)
            )
            .ConfigureAwait(false);

    /// <summary>
    /// Checks if the transport is healthy and ready to send messages.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns><c>true</c> if the transport is healthy; otherwise, <c>false</c>.</returns>
    /// <remarks>
    /// <para><strong>Used by the outbox processor:</strong></para>
    /// The processor checks transport health before each processing cycle. If unhealthy,
    /// the cycle is skipped and retried after the polling interval.
    /// <para><strong>Default Implementation:</strong></para>
    /// Returns <c>true</c> by default. Override to implement actual health checks for message brokers,
    /// HTTP endpoints, or other external dependencies.
    /// <para><strong>External Integration:</strong></para>
    /// Can also be used with Microsoft.Extensions.Diagnostics.HealthChecks for application health monitoring.
    /// </remarks>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
}
