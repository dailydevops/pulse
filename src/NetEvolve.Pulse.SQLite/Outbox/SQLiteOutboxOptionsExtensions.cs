namespace NetEvolve.Pulse.Outbox;

/// <summary>
/// Extension methods for <see cref="SQLiteOutboxOptions"/> to provide additional functionality
/// related to SQLite outbox implementations.
/// </summary>
internal static class SQLiteOutboxOptionsExtensions
{
    extension(SQLiteOutboxOptions options)
    {
        /// <summary>
        /// Gets the fully qualified (quoted) table name for use in SQL statements.
        /// </summary>
        public string FullTableName => $"\"{options.TableName}\"";
    }
}
