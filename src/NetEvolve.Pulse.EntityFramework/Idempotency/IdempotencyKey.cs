namespace NetEvolve.Pulse.Idempotency;

using System;

/// <summary>
/// Represents a persisted idempotency key, enabling duplicate command detection.
/// This entity serves as the canonical schema contract for the EF Core idempotency store.
/// </summary>
/// <remarks>
/// <para><strong>Schema Contract:</strong></para>
/// All persistence implementations MUST use identical column names and types
/// to ensure interchangeability.
/// <para><strong>Column Specifications:</strong></para>
/// <list type="bullet">
/// <item><description><see cref="Key"/>: VARCHAR(500), Primary Key — the client-supplied idempotency key.</description></item>
/// <item><description><see cref="CreatedAt"/>: DATETIMEOFFSET, NOT NULL — timestamp when the key was stored.</description></item>
/// </list>
/// </remarks>
public sealed class IdempotencyKey
{
    /// <summary>
    /// Gets or sets the idempotency key string. Acts as the primary key.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the UTC timestamp when this key was first stored.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}
