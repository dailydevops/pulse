namespace NetEvolve.Pulse;

using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Extension methods for <see cref="OutboxOptions"/> to provide additional functionality related to SQL Server outbox implementations.
/// </summary>
internal static class SqlServerOutboxOptionsExtensions
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
                return $"[{schema}].[{options.TableName}]";
            }
        }
    }
}
