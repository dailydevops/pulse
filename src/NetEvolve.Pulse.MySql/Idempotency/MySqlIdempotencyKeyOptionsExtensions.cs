namespace NetEvolve.Pulse.Idempotency;

/// <summary>
/// Extension methods for <see cref="IdempotencyKeyOptions"/> to provide additional functionality
/// related to MySQL idempotency implementations.
/// </summary>
internal static class MySqlIdempotencyKeyOptionsExtensions
{
    extension(IdempotencyKeyOptions options)
    {
        /// <summary>
        /// Gets the fully qualified (backtick-quoted) table name for use in SQL statements.
        /// </summary>
        /// <remarks>
        /// MySQL does not use schema namespaces in the same way as SQL Server or PostgreSQL.
        /// Tables are always created in the active database from the connection string.
        /// The <see cref="IdempotencyKeyOptions.Schema"/> property is not used for MySQL.
        /// </remarks>
        public string FullTableName => $"`{options.TableName}`";
    }
}
