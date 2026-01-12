namespace NetEvolve.Pulse.Extensibility;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Defines the canonical database schema for outbox messages.
/// All persistence providers MUST use these constants to ensure interchangeability.
/// </summary>
public static class OutboxMessageSchema
{
    /// <summary>
    /// Default schema name for the outbox table.
    /// </summary>
    public const string DefaultSchema = "pulse";

    /// <summary>
    /// Default table name for the outbox messages.
    /// </summary>
    public const string DefaultTableName = "OutboxMessage";

    /// <summary>
    /// Column name constants matching <see cref="OutboxMessage"/> properties.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1034:Nested types should not be visible",
        Justification = "Intentionally nested for organizational clarity - constants grouped by purpose."
    )]
    public static class Columns
    {
        /// <summary>
        /// The Id column name.
        /// </summary>
        public const string Id = "Id";

        /// <summary>
        /// The EventType column name.
        /// </summary>
        public const string EventType = "EventType";

        /// <summary>
        /// The Payload column name.
        /// </summary>
        public const string Payload = "Payload";

        /// <summary>
        /// The CorrelationId column name.
        /// </summary>
        public const string CorrelationId = "CorrelationId";

        /// <summary>
        /// The CreatedAt column name.
        /// </summary>
        public const string CreatedAt = "CreatedAt";

        /// <summary>
        /// The UpdatedAt column name.
        /// </summary>
        public const string UpdatedAt = "UpdatedAt";

        /// <summary>
        /// The ProcessedAt column name.
        /// </summary>
        public const string ProcessedAt = "ProcessedAt";

        /// <summary>
        /// The RetryCount column name.
        /// </summary>
        public const string RetryCount = "RetryCount";

        /// <summary>
        /// The Error column name.
        /// </summary>
        public const string Error = "Error";

        /// <summary>
        /// The Status column name.
        /// </summary>
        public const string Status = "Status";
    }

    /// <summary>
    /// Recommended maximum lengths for string columns.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1034:Nested types should not be visible",
        Justification = "Intentionally nested for organizational clarity - constants grouped by purpose."
    )]
    public static class MaxLengths
    {
        /// <summary>
        /// Maximum length for EventType column (500 characters).
        /// </summary>
        public const int EventType = 500;

        /// <summary>
        /// Maximum length for CorrelationId column (100 characters).
        /// </summary>
        public const int CorrelationId = 100;
    }
}
