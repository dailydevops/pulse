namespace NetEvolve.Pulse.Extensibility.DeadLetter;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Defines the canonical database schema for command dead letter entries.
/// All persistence providers MUST use these constants to ensure interchangeability.
/// </summary>
public static class CommandDeadLetterSchema
{
    /// <summary>
    /// Default schema name for the command dead letter table.
    /// </summary>
    public const string DefaultSchema = "pulse";

    /// <summary>
    /// Default table name for the command dead letter entries.
    /// </summary>
    public const string DefaultTableName = "CommandDeadLetter";

    /// <summary>
    /// Column name constants matching <see cref="CommandDeadLetterEntry"/> properties.
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
        /// The Payload column name.
        /// </summary>
        public const string Payload = "Payload";

        /// <summary>
        /// The ExceptionType column name.
        /// </summary>
        public const string ExceptionType = "ExceptionType";

        /// <summary>
        /// The ExceptionMessage column name.
        /// </summary>
        public const string ExceptionMessage = "ExceptionMessage";

        /// <summary>
        /// The OccurredAt column name.
        /// </summary>
        public const string OccurredAt = "OccurredAt";

        /// <summary>
        /// The AttemptCount column name.
        /// </summary>
        public const string AttemptCount = "AttemptCount";

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
        /// Maximum length for CommandType column (500 characters).
        /// </summary>
        public const int CommandType = 500;

        /// <summary>
        /// Maximum length for ExceptionType column (500 characters).
        /// </summary>
        public const int ExceptionType = 500;
    }
}
