namespace NetEvolve.Pulse.Idempotency;

using NetEvolve.Pulse.Extensibility.Outbox;

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
        /// <exception cref="System.ArgumentException">
        /// Thrown when <see cref="IdempotencyKeyOptions.TableName"/> contains characters outside
        /// the safe SQL-identifier subset enforced by <see cref="SqlIdentifier.Validate"/>.
        /// </exception>
        public string FullTableName
        {
            get
            {
                SqlIdentifier.Validate(options.TableName, nameof(options.TableName));
                return $"`{options.TableName}`";
            }
        }
    }
}
