namespace NetEvolve.Pulse.Outbox;

/// <summary>
/// Extension methods for <see cref="OutboxOptions"/> to provide additional functionality
/// related to SQLite outbox implementations.
/// </summary>
internal static class OutboxOptionsExtensions
{
    extension(OutboxOptions options)
    {
        /// <summary>
        /// Gets the fully qualified (quoted) table name for use in SQL statements.
        /// </summary>
        public string FullTableName => $"\"{options.TableName}\"";
    }
}
