namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Defines the contract for outbox message persistence operations.
/// Implementations provide storage-specific CRUD operations for outbox messages.
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong></para>
/// The outbox repository abstracts persistence operations, allowing different storage backends
/// (SQL Server, PostgreSQL, Entity Framework, etc.) while maintaining a consistent API.
/// <para><strong>Thread Safety:</strong></para>
/// Implementations SHOULD be thread-safe for concurrent access from the background processor.
/// <para><strong>Transaction Handling:</strong></para>
/// <list type="bullet">
/// <item><description><see cref="AddAsync"/> SHOULD participate in ambient transactions when available</description></item>
/// <item><description>Other methods are typically called outside transaction scope by the background processor</description></item>
/// </list>
/// </remarks>
public interface IOutboxRepository
{
    /// <summary>
    /// Adds a new message to the outbox.
    /// </summary>
    /// <param name="message">The message to add.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// This method SHOULD participate in any ambient transaction to ensure atomicity
    /// with business operations.
    /// </remarks>
    Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves pending messages for processing.
    /// </summary>
    /// <param name="batchSize">Maximum number of messages to retrieve.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of pending messages ordered by creation time.</returns>
    /// <remarks>
    /// Implementations SHOULD lock the retrieved messages (e.g., by setting status to Processing)
    /// to prevent concurrent processing of the same message.
    /// </remarks>
    Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int batchSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a message as successfully completed.
    /// </summary>
    /// <param name="messageId">The ID of the message to mark as completed.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task MarkAsCompletedAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a message as failed and records the error.
    /// </summary>
    /// <param name="messageId">The ID of the message that failed.</param>
    /// <param name="errorMessage">The error message or exception details.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Implementations SHOULD increment the retry count and update the status accordingly.
    /// </remarks>
    Task MarkAsFailedAsync(Guid messageId, string errorMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a message to dead letter status after exceeding retry limits.
    /// </summary>
    /// <param name="messageId">The ID of the message to move to dead letter.</param>
    /// <param name="errorMessage">The final error message.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task MarkAsDeadLetterAsync(Guid messageId, string errorMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes completed messages older than the specified retention period.
    /// </summary>
    /// <param name="olderThan">The age threshold for deletion.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The number of messages deleted.</returns>
    Task<int> DeleteCompletedAsync(TimeSpan olderThan, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves messages eligible for retry based on retry count and delay.
    /// </summary>
    /// <param name="maxRetryCount">Maximum retry count threshold.</param>
    /// <param name="batchSize">Maximum number of messages to retrieve.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A collection of failed messages eligible for retry.</returns>
    Task<IReadOnlyList<OutboxMessage>> GetFailedForRetryAsync(
        int maxRetryCount,
        int batchSize,
        CancellationToken cancellationToken = default
    );
}
