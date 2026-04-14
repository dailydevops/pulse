namespace NetEvolve.Pulse.Extensibility.Idempotency;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Defines the canonical database schema for idempotency keys.
/// All persistence providers MUST use these constants to ensure interchangeability.
/// </summary>
public static class IdempotencyKeySchema
{
    /// <summary>
    /// Default schema name for the idempotency key table.
    /// </summary>
    public const string DefaultSchema = "pulse";

    /// <summary>
    /// Default table name for the idempotency keys.
    /// </summary>
    public const string DefaultTableName = "IdempotencyKey";

    /// <summary>
    /// Column name constants matching <see cref="IdempotencyKeySchema"/> properties.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1034:Nested types should not be visible",
        Justification = "Intentionally nested for organizational clarity - constants grouped by purpose."
    )]
    public static class Columns
    {
        /// <summary>
        /// The IdempotencyKey column name.
        /// </summary>
        public const string IdempotencyKey = "IdempotencyKey";

        /// <summary>
        /// The CreatedAt column name.
        /// </summary>
        public const string CreatedAt = "CreatedAt";
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
        /// Maximum length for the IdempotencyKey column (500 characters).
        /// </summary>
        public const int IdempotencyKey = 500;
    }
}
