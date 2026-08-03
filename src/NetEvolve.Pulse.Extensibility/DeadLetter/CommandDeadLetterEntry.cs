namespace NetEvolve.Pulse.Extensibility.DeadLetter;

/// <summary>
/// Represents a command that failed processing and was moved to the dead letter store.
/// This entity serves as the canonical schema contract shared across all persistence providers.
/// </summary>
/// <remarks>
/// <para><strong>Schema Contract:</strong></para>
/// All persistence implementations (SQL Server, Entity Framework, etc.) MUST use
/// identical column names and types to ensure interchangeability.
/// <para><strong>Column Specifications:</strong></para>
/// <list type="bullet">
/// <item><description><see cref="Id"/>: UNIQUEIDENTIFIER / GUID, Primary Key</description></item>
/// <item><description><see cref="CommandType"/>: NVARCHAR(500), NOT NULL - Runtime type; persisted as assembly-qualified name</description></item>
/// <item><description><see cref="Payload"/>: NVARCHAR(MAX) / TEXT, NOT NULL - JSON serialized command</description></item>
/// <item><description><see cref="ExceptionType"/>: NVARCHAR(500), NULL - Assembly-qualified name of the exception type that caused the failure</description></item>
/// <item><description><see cref="ExceptionMessage"/>: NVARCHAR(MAX) / TEXT, NULL - Message of the exception that caused the failure</description></item>
/// <item><description><see cref="OccurredAt"/>: DATETIMEOFFSET, NOT NULL - Timestamp the failure was recorded</description></item>
/// <item><description><see cref="AttemptCount"/>: INT, NOT NULL, DEFAULT 1 - Number of processing attempts</description></item>
/// <item><description><see cref="Status"/>: INT, NOT NULL, DEFAULT 0 - Dead letter status enum value</description></item>
/// </list>
/// </remarks>
public sealed class CommandDeadLetterEntry
{
    /// <summary>
    /// Gets or sets the unique identifier for this dead letter entry.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the assembly-qualified type name of the command that failed processing.
    /// </summary>
    /// <remarks>
    /// Stored in the database as a string (maximum 500 characters).
    /// Used to resolve the runtime <see cref="Type"/> when replaying the command.
    /// </remarks>
    public string CommandType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the JSON serialized command payload.
    /// </summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the assembly-qualified type name of the exception that caused the failure.
    /// </summary>
    /// <remarks>
    /// Maximum length: 500 characters.
    /// </remarks>
    public string? ExceptionType { get; set; }

    /// <summary>
    /// Gets or sets the message of the exception that caused the failure.
    /// </summary>
    public string? ExceptionMessage { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when this entry was recorded.
    /// </summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>
    /// Gets or sets the number of times processing of this command has been attempted.
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// Gets or sets the current dead letter status.
    /// </summary>
    public CommandDeadLetterStatus Status { get; set; } = CommandDeadLetterStatus.New;
}
