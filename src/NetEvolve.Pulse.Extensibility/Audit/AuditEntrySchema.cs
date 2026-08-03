namespace NetEvolve.Pulse.Extensibility.Audit;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Defines the canonical database schema for audit trail entries.
/// All persistence providers MUST use these constants to ensure interchangeability.
/// </summary>
public static class AuditEntrySchema
{
    /// <summary>
    /// Default schema name for the audit entry table.
    /// </summary>
    public const string DefaultSchema = "pulse";

    /// <summary>
    /// Default table name for the audit entries.
    /// </summary>
    public const string DefaultTableName = "AuditEntry";

    /// <summary>
    /// Column name constants matching <see cref="AuditRecord"/> properties.
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
        /// The CommandType column name.
        /// </summary>
        public const string CommandType = "CommandType";

        /// <summary>
        /// The UserId column name.
        /// </summary>
        public const string UserId = "UserId";

        /// <summary>
        /// The CorrelationId column name.
        /// </summary>
        public const string CorrelationId = "CorrelationId";

        /// <summary>
        /// The OccurredAt column name.
        /// </summary>
        public const string OccurredAt = "OccurredAt";

        /// <summary>
        /// The DurationMs column name.
        /// </summary>
        public const string DurationMs = "DurationMs";

        /// <summary>
        /// The Result column name.
        /// </summary>
        public const string Result = "Result";

        /// <summary>
        /// The Payload column name.
        /// </summary>
        public const string Payload = "Payload";

        /// <summary>
        /// The ExceptionMessage column name.
        /// </summary>
        public const string ExceptionMessage = "ExceptionMessage";
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
        /// Maximum length for CommandType column (500 characters).
        /// </summary>
        public const int CommandType = 500;

        /// <summary>
        /// Maximum length for UserId column (256 characters).
        /// </summary>
        public const int UserId = 256;

        /// <summary>
        /// Maximum length for CorrelationId column (100 characters).
        /// </summary>
        public const int CorrelationId = 100;
    }
}

/// <summary>
/// Represents the outcome of a request that was recorded to the audit trail.
/// </summary>
public enum AuditResult
{
    /// <summary>
    /// The request completed successfully.
    /// </summary>
    Success = 0,

    /// <summary>
    /// The request failed, i.e. the handler threw an exception.
    /// </summary>
    Failure = 1,
}
