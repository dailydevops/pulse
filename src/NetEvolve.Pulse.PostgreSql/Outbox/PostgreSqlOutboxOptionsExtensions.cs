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
        public string FullTableName
        {
            get
            {
                var schema = string.IsNullOrWhiteSpace(options.Schema)
                    ? OutboxMessageSchema.DefaultSchema
                    : options.Schema.Trim();
                return $"\"{schema}\".\"{options.TableName}\"";
            }
        }
    }
}
