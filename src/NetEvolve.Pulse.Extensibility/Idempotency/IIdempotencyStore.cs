namespace NetEvolve.Pulse.Extensibility.Idempotency;

/// <summary>
/// Defines the contract for a store that tracks processed idempotency keys, enabling
/// duplicate command detection in the idempotency interceptor pipeline.
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong></para>
/// The idempotency store persists keys that represent commands already processed to completion.
/// Before a command is executed, the interceptor checks whether its key already exists in the store.
/// After successful execution the key is written, so any subsequent retry with the same key is rejected.
/// <para><strong>Implementation Guidelines:</strong></para>
/// <list type="bullet">
/// <item><description>Implementations MUST be thread-safe — multiple concurrent requests may read/write simultaneously.</description></item>
/// <item><description>Implementations SHOULD support an expiry or time-to-live mechanism to prevent unbounded growth.</description></item>
/// <item><description>Implementations SHOULD use distributed storage (Redis, SQL, etc.) in multi-instance deployments.</description></item>
/// </list>
/// <para><strong>Atomicity Note:</strong></para>
/// The interceptor reserves the key via <see cref="TryReserveAsync"/> BEFORE the handler executes.
/// The default implementation of <see cref="TryReserveAsync"/> composes <see cref="ExistsAsync"/> and
/// <see cref="StoreAsync"/> and is therefore not atomic. If strict at-most-once semantics are required,
/// implementations SHOULD override <see cref="TryReserveAsync"/> with an atomic check-and-set operation
/// (e.g., via a database unique constraint or a Redis SET NX).
/// </remarks>
/// <example>
/// <code>
/// // In-memory implementation for testing
/// public sealed class InMemoryIdempotencyStore : IIdempotencyStore
/// {
///     private readonly HashSet&lt;string&gt; _keys = [];
///
///     public Task&lt;bool&gt; ExistsAsync(string idempotencyKey, CancellationToken cancellationToken = default)
///         =&gt; Task.FromResult(_keys.Contains(idempotencyKey));
///
///     public Task StoreAsync(string idempotencyKey, CancellationToken cancellationToken = default)
///     {
///         _keys.Add(idempotencyKey);
///         return Task.CompletedTask;
///     }
/// }
/// </code>
/// </example>
/// <seealso cref="IIdempotentCommand{TResponse}"/>
public interface IIdempotencyStore
{
    /// <summary>
    /// Determines whether the specified idempotency key has already been stored,
    /// indicating that the corresponding command was previously processed.
    /// </summary>
    /// <param name="idempotencyKey">The idempotency key to look up. Must not be <see langword="null"/> or empty.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// <see langword="true"/> if the key is already present in the store; otherwise <see langword="false"/>.
    /// </returns>
    Task<bool> ExistsAsync(string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the specified idempotency key so that future calls to <see cref="ExistsAsync"/>
    /// with the same key return <see langword="true"/>.
    /// </summary>
    /// <param name="idempotencyKey">The idempotency key to store. Must not be <see langword="null"/> or empty.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous store operation.</returns>
    Task StoreAsync(string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to reserve the specified idempotency key before the corresponding command executes,
    /// so that concurrent or subsequent submissions of the same key are rejected.
    /// </summary>
    /// <param name="idempotencyKey">The idempotency key to reserve. Must not be <see langword="null"/> or empty.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// <see langword="true"/> if the key was newly reserved and the command may execute;
    /// <see langword="false"/> if the key is already present, indicating a duplicate submission.
    /// </returns>
    /// <remarks>
    /// The default implementation composes <see cref="ExistsAsync"/> and <see cref="StoreAsync"/> and
    /// is therefore not atomic — two concurrent calls may both observe an absent key within the small
    /// window between the two operations. Implementations backed by storage that supports an atomic
    /// check-and-set (database unique constraint, Redis <c>SET NX</c>) SHOULD override this member to
    /// close that window and provide strict at-most-once semantics.
    /// </remarks>
    async Task<bool> TryReserveAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (await ExistsAsync(idempotencyKey, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await StoreAsync(idempotencyKey, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
