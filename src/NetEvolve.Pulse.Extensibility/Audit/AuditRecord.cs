namespace NetEvolve.Pulse.Extensibility.Audit;

/// <summary>
/// Represents a single audit trail entry recorded for a processed request.
/// This entity serves as the canonical schema contract shared across all persistence providers.
/// </summary>
/// <remarks>
/// <para><strong>Schema Contract:</strong></para>
/// All persistence implementations (SQL Server, Entity Framework, etc.) MUST use
/// identical column names and types to ensure interchangeability. This type is reused
/// directly as the Entity Framework Core entity for the audit trail store, in the same
/// manner <c>NetEvolve.Pulse.Extensibility.Outbox.OutboxMessage</c> is reused directly as
/// the EF entity for the outbox, and <c>CommandDeadLetterEntry</c> is reused directly as
/// the EF entity for the command dead letter store.
/// <para><strong>Column Specifications:</strong></para>
/// <list type="bullet">
/// <item><description><see cref="Id"/>: UNIQUEIDENTIFIER / GUID, Primary Key</description></item>
/// <item><description><see cref="CommandType"/>: NVARCHAR(500), NOT NULL - Runtime type name of the request</description></item>
/// <item><description><see cref="UserId"/>: NVARCHAR(256), NULL - Identifier of the user who issued the request</description></item>
/// <item><description><see cref="CorrelationId"/>: NVARCHAR(100), NULL - Correlation identifier associated with the request</description></item>
/// <item><description><see cref="OccurredAt"/>: DATETIMEOFFSET, NOT NULL - Timestamp the request was recorded</description></item>
/// <item><description><see cref="DurationMs"/>: FLOAT, NOT NULL - Elapsed time, in milliseconds, of the handler invocation</description></item>
/// <item><description><see cref="Result"/>: INT, NOT NULL - Audit result enum value</description></item>
/// <item><description><see cref="Payload"/>: NVARCHAR(MAX) / TEXT, NULL - JSON serialized request payload, only populated when capturing payloads is enabled</description></item>
/// <item><description><see cref="ExceptionMessage"/>: NVARCHAR(MAX) / TEXT, NULL - Message of the exception that caused the failure</description></item>
/// </list>
/// </remarks>
public sealed class AuditRecord
{
    /// <summary>
    /// Gets or sets the unique identifier for this audit record.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the type name of the request that was processed.
    /// </summary>
    /// <remarks>
    /// Stored in the database as a string (maximum 500 characters).
    /// </remarks>
    public string CommandType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the identifier of the user who issued the request.
    /// </summary>
    /// <remarks>
    /// Maximum length: 256 characters. <see langword="null"/> when no user could be resolved.
    /// </remarks>
    public string? UserId { get; set; }

    /// <summary>
    /// Gets or sets the correlation identifier associated with the request.
    /// </summary>
    /// <remarks>
    /// Maximum length: 100 characters.
    /// </remarks>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when this request was recorded.
    /// </summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>
    /// Gets or sets the elapsed time, in milliseconds, of the handler invocation.
    /// </summary>
    public double DurationMs { get; set; }

    /// <summary>
    /// Gets or sets the outcome of the request.
    /// </summary>
    public AuditResult Result { get; set; }

    /// <summary>
    /// Gets or sets the JSON serialized request payload.
    /// </summary>
    /// <remarks>
    /// Only populated when <c>AuditOptions.CapturePayload</c> is set to <see langword="true"/>.
    /// </remarks>
    public string? Payload { get; set; }

    /// <summary>
    /// Gets or sets the message of the exception that caused the failure.
    /// </summary>
    /// <remarks>
    /// Only populated when <see cref="Result"/> is <see cref="AuditResult.Failure"/>.
    /// </remarks>
    public string? ExceptionMessage { get; set; }
}
