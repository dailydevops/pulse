namespace NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Represents a message stored in the outbox for reliable delivery.
/// This entity serves as the canonical schema contract shared across all persistence providers.
/// </summary>
/// <remarks>
/// <para><strong>Schema Contract:</strong></para>
/// All persistence implementations (SQL Server, Entity Framework, etc.) MUST use
/// identical column names and types to ensure interchangeability.
/// <para><strong>Column Specifications:</strong></para>
/// <list type="bullet">
/// <item><description><see cref="Id"/>: UNIQUEIDENTIFIER / GUID, Primary Key</description></item>
/// <item><description><see cref="EventType"/>: NVARCHAR(500), NOT NULL - Runtime type; persisted as assembly-qualified name</description></item>
/// <item><description><see cref="Payload"/>: NVARCHAR(MAX) / TEXT, NOT NULL - JSON serialized event</description></item>
/// <item><description><see cref="CorrelationId"/>: NVARCHAR(100), NULL - Distributed tracing correlation</description></item>
/// <item><description><see cref="CausationId"/>: NVARCHAR(100), NULL - Causal chain tracking</description></item>
/// <item><description><see cref="CreatedAt"/>: DATETIMEOFFSET, NOT NULL - Message creation timestamp</description></item>
/// <item><description><see cref="UpdatedAt"/>: DATETIMEOFFSET, NOT NULL - Last modification timestamp</description></item>
/// <item><description><see cref="ProcessedAt"/>: DATETIMEOFFSET, NULL - Successful processing timestamp</description></item>
/// <item><description><see cref="RetryCount"/>: INT, NOT NULL, DEFAULT 0 - Number of processing attempts</description></item>
/// <item><description><see cref="Error"/>: NVARCHAR(MAX), NULL - Last error message</description></item>
/// <item><description><see cref="Status"/>: INT, NOT NULL, DEFAULT 0 - Processing status enum value</description></item>
/// <item><description><see cref="NextRetryAt"/>: DATETIMEOFFSET, NULL - Scheduled retry timestamp, set whenever a message moves to failed status</description></item>
/// </list>
/// </remarks>
public sealed class OutboxMessage
{
    /// <summary>
    /// Gets or sets the unique identifier for this outbox message.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the runtime type of the event.
    /// Used for deserialization and routing.
    /// </summary>
    /// <remarks>
    /// Stored in the database as the assembly-qualified type name (maximum 500 characters).
    /// Persistence providers are responsible for converting this value to and from its string representation.
    /// </remarks>
    public Type EventType { get; set; } = null!;

    /// <summary>
    /// Gets or sets the JSON serialized event payload.
    /// </summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the correlation identifier for distributed tracing.
    /// </summary>
    /// <remarks>
    /// Maximum length: 100 characters.
    /// </remarks>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Gets or sets the causation identifier that records which command or event directly caused this message.
    /// </summary>
    /// <remarks>
    /// Maximum length: 100 characters.
    /// </remarks>
    public string? CausationId { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when this message was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when this message was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when this message was successfully processed.
    /// Null if not yet processed or if processing failed.
    /// </summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>
    /// Gets or sets the scheduled timestamp for the next retry attempt.
    /// Null when the message is not in failed status, or for rows written by older versions.
    /// </summary>
    /// <remarks>
    /// The processor sets this value whenever a message transitions to Failed status: the backoff schedule
    /// when <c>EnableExponentialBackoff</c> is true, otherwise one <c>PollingInterval</c> after the failure.
    /// Repositories MUST NOT return a failed message for retry before this time.
    /// </remarks>
    public DateTimeOffset? NextRetryAt { get; set; }

    /// <summary>
    /// Gets or sets the number of times processing has been attempted.
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Gets or sets the last error message from a failed processing attempt.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Gets or sets the current processing status.
    /// </summary>
    public OutboxMessageStatus Status { get; set; } = OutboxMessageStatus.Pending;
}
