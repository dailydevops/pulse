namespace NetEvolve.Pulse.Idempotency;

/// <summary>
/// Extension methods for <see cref="IdempotencyKeyOptions"/> to provide additional functionality
/// related to SQLite idempotency implementations.
/// </summary>
internal static class IdempotencyKeyOptionsExtensions
{
    extension(IdempotencyKeyOptions options)
    {
        /// <summary>
        /// Gets the fully qualified (quoted) table name for use in SQL statements.
        /// </summary>
        public string FullTableName => $"\"{options.TableName}\"";
    }
}
