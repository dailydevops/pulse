namespace NetEvolve.Pulse.Outbox;

using NetEvolve.Pulse.Extensibility.Outbox;

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
        /// <exception cref="System.ArgumentException">
        /// Thrown when <see cref="OutboxOptions.TableName"/> contains characters outside the
        /// safe SQL-identifier subset enforced by <see cref="SqlIdentifier.Validate"/>.
        /// </exception>
        public string FullTableName
        {
            get
            {
                SqlIdentifier.Validate(options.TableName, nameof(options.TableName));
                return $"\"{options.TableName}\"";
            }
        }
    }
}
