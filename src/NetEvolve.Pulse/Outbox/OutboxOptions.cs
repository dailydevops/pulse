namespace NetEvolve.Pulse.Outbox;

using System.Text.Json;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Configuration options for the outbox pattern implementation.
/// </summary>
public sealed class OutboxOptions
{
    /// <summary>
    /// Gets or sets the database schema name for the outbox table.
    /// Default: "pulse".
    /// </summary>
    /// <remarks>
    /// Set to <c>null</c> or empty string to use the default schema (e.g., "dbo" for SQL Server).
    /// Some database providers may not support schemas.
    /// </remarks>
    public string? Schema { get; set; } = OutboxMessageSchema.DefaultSchema;

    /// <summary>
    /// Gets or sets the table name for outbox messages.
    /// Default: "OutboxMessage".
    /// </summary>
    public string TableName { get; set; } = OutboxMessageSchema.DefaultTableName;

    /// <summary>
    /// Gets the fully qualified table name including schema.
    /// </summary>
    public string FullTableName => string.IsNullOrWhiteSpace(Schema) ? $"[{TableName}]" : $"[{Schema}].[{TableName}]";

    /// <summary>
    /// Gets or sets the JSON serializer options for event serialization.
    /// </summary>
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }
}
