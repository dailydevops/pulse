namespace NetEvolve.Pulse.Idempotency;

using System;
using NetEvolve.Pulse.Extensibility.Idempotency;

/// <summary>
/// Configuration options for the Entity Framework Core idempotency store.
/// </summary>
public sealed class IdempotencyKeyOptions
{
    /// <summary>
    /// Gets or sets the database schema name for the idempotency key table.
    /// Default: <c>"pulse"</c>.
    /// </summary>
    /// <remarks>
    /// Set to <c>null</c> or empty string to use the default schema (e.g., "dbo" for SQL Server).
    /// Some database providers may not support schemas.
    /// </remarks>
    public string? Schema { get; set; } = IdempotencyKeySchema.DefaultSchema;

    /// <summary>
    /// Gets or sets the table name for idempotency keys.
    /// Default: <c>"IdempotencyKey"</c>.
    /// </summary>
    public string TableName { get; set; } = IdempotencyKeySchema.DefaultTableName;

    /// <summary>
    /// Gets or sets the time-to-live for stored idempotency keys.
    /// When set, keys older than this duration are treated as absent by <see cref="IIdempotencyStore.ExistsAsync"/>.
    /// When <see langword="null"/>, keys never expire.
    /// </summary>
    /// <remarks>
    /// TTL-based cleanup (physical row deletion) is out of scope and must be handled externally.
    /// This option only controls whether expired keys are logically treated as absent.
    /// </remarks>
    public TimeSpan? TimeToLive { get; set; }
}
