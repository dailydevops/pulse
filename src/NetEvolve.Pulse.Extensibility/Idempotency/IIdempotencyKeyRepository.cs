namespace NetEvolve.Pulse.Extensibility.Idempotency;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Defines the contract for idempotency key persistence operations.
/// Implementations provide storage-specific operations for idempotency keys.
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong></para>
/// The idempotency key repository abstracts the persistence of idempotency keys,
/// allowing different storage backends (SQL Server, PostgreSQL, Entity Framework, etc.)
/// while maintaining a consistent API.
/// <para><strong>Usage:</strong></para>
/// This interface is the low-level storage contract. The central <see cref="IIdempotencyStore"/>
/// implementation delegates to this repository, applying higher-level logic such as time-to-live filtering.
/// </remarks>
/// <seealso cref="IIdempotencyStore"/>
public interface IIdempotencyKeyRepository
{
    /// <summary>
    /// Determines whether an idempotency key exists in the store, optionally filtering
    /// by a minimum creation timestamp (time-to-live boundary).
    /// </summary>
    /// <param name="idempotencyKey">The idempotency key to check.</param>
    /// <param name="validFrom">
    /// When set, only keys created at or after this timestamp are considered as existing.
    /// Keys older than this cutoff are treated as absent. When <see langword="null"/>, all matching
    /// keys are returned regardless of age.
    /// </param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// <see langword="true"/> if a matching key exists (and, when <paramref name="validFrom"/> is set,
    /// has not expired); otherwise <see langword="false"/>.
    /// </returns>
    Task<bool> ExistsAsync(
        string idempotencyKey,
        DateTimeOffset? validFrom = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Persists an idempotency key with the specified creation timestamp.
    /// </summary>
    /// <param name="idempotencyKey">The idempotency key to store.</param>
    /// <param name="createdAt">The timestamp to associate with the stored key.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous store operation.</returns>
    /// <remarks>
    /// Implementations MUST handle duplicate-key exceptions gracefully and treat them as
    /// a successful (idempotent) store operation.
    /// </remarks>
    Task StoreAsync(string idempotencyKey, DateTimeOffset createdAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to store an idempotency key only if no valid key with the same value exists yet.
    /// </summary>
    /// <param name="idempotencyKey">The idempotency key to reserve.</param>
    /// <param name="createdAt">The timestamp to associate with the stored key.</param>
    /// <param name="validFrom">
    /// When set, an existing key created before this timestamp is treated as absent and may be
    /// replaced by this reservation. When <see langword="null"/>, an existing key is never replaced.
    /// </param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// <see langword="true"/> if this call stored the key; <see langword="false"/> if a valid key already exists.
    /// </returns>
    /// <remarks>
    /// Implementations backed by storage that supports an atomic check-and-set (for example Redis <c>SET NX</c>)
    /// SHOULD perform the check and the store as one atomic operation, so that of several concurrent calls
    /// for the same key at most one returns <see langword="true"/>. Implementations that compose
    /// <see cref="ExistsAsync"/> and <see cref="StoreAsync"/> are not atomic.
    /// </remarks>
    Task<bool> TryStoreAsync(
        string idempotencyKey,
        DateTimeOffset createdAt,
        DateTimeOffset? validFrom = null,
        CancellationToken cancellationToken = default
    );
}
