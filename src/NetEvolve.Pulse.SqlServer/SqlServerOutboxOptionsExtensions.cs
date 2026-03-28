namespace NetEvolve.Pulse;

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
        public string FullTableName =>
            string.IsNullOrWhiteSpace(options.Schema)
                ? $"[{options.TableName}]"
                : $"[{options.Schema}].[{options.TableName}]";
    }
}
