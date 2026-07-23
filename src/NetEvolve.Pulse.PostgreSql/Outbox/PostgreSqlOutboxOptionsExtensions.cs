namespace NetEvolve.Pulse.Outbox;

using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Extension methods for <see cref="OutboxOptions"/> to provide additional functionality related to PostgreSQL outbox implementations.
/// </summary>
internal static class PostgreSqlOutboxOptionsExtensions
{
    extension(OutboxOptions options)
    {
        /// <summary>
        /// Gets the fully qualified table name including schema.
        /// </summary>
        /// <exception cref="System.ArgumentException">
        /// Thrown when <see cref="OutboxOptions.Schema"/> or <see cref="OutboxOptions.TableName"/>
        /// contains characters outside the safe SQL-identifier subset enforced by
        /// <see cref="SqlIdentifier.Validate"/>. This guards against injection through
        /// configuration-supplied option values that would otherwise be interpolated raw
        /// into <c>"schema"."table"</c> syntax.
        /// </exception>
        public string FullTableName
        {
            get
            {
                var schema = string.IsNullOrWhiteSpace(options.Schema)
                    ? OutboxMessageSchema.DefaultSchema
                    : options.Schema.Trim();
                SqlIdentifier.Validate(schema, nameof(options.Schema));
                SqlIdentifier.Validate(options.TableName, nameof(options.TableName));
                return $"\"{schema}\".\"{options.TableName}\"";
            }
        }
    }
}
