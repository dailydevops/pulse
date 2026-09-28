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
    /// Atomically reserves an idempotency key: inserts it when absent, or refreshes its creation
    /// timestamp when the stored key has expired.
    /// </summary>
    /// <param name="idempotencyKey">The idempotency key to reserve.</param>
    /// <param name="createdAt">The timestamp to associate with the reserved key.</param>
    /// <param name="validFrom">
    /// When set, a stored key created before this timestamp is expired and is overwritten with
    /// <paramref name="createdAt"/>. When <see langword="null"/>, keys never expire and an existing
    /// key is never modified.
    /// </param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// <see langword="true"/> if the key was inserted or an expired key was refreshed;
    /// <see langword="false"/> if a key that has not expired already exists (duplicate submission).
    /// </returns>
    /// <remarks>
    /// Implementations MUST perform the check and the write as one atomic operation, so that exactly
    /// one of several concurrent callers for the same key receives <see langword="true"/>, and MUST NOT
    /// modify a key that has not expired.
    /// </remarks>
    Task<bool> TryReserveAsync(
        string idempotencyKey,
        DateTimeOffset createdAt,
        DateTimeOffset? validFrom = null,
        CancellationToken cancellationToken = default
    );
}
