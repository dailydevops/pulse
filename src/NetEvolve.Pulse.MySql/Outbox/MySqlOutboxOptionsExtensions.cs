namespace NetEvolve.Pulse.Outbox;

using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Extension methods for <see cref="OutboxOptions"/> to provide additional functionality
/// related to MySQL outbox implementations.
/// </summary>
internal static class MySqlOutboxOptionsExtensions
{
    extension(OutboxOptions options)
    {
        /// <summary>
        /// Gets the fully qualified (backtick-quoted) table name for use in SQL statements.
        /// </summary>
        /// <remarks>
        /// MySQL does not use schema namespaces in the same way as SQL Server or PostgreSQL.
        /// Tables are always created in the active database from the connection string.
        /// The <see cref="OutboxOptions.Schema"/> property is not used for MySQL.
        /// </remarks>
        /// <exception cref="System.ArgumentException">
        /// Thrown when <see cref="OutboxOptions.TableName"/> contains characters outside the
        /// safe SQL-identifier subset enforced by <see cref="SqlIdentifier.Validate"/>.
        /// This guards against injection through configuration-supplied option values that
        /// would otherwise be interpolated raw into backtick-quoted syntax.
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
