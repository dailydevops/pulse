namespace NetEvolve.Pulse.Outbox;

using System.Text.Json;
using NetEvolve.Pulse.Extensibility.Outbox;

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
    /// Gets or sets the outbox database connection string.
    /// </summary>
    /// <remarks>
    /// This setting is used by ADO.NET-based providers such as PostgreSQL, SQL Server, and SQLite.
    /// </remarks>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether WAL (Write-Ahead Logging) mode is enabled.
    /// </summary>
    /// <remarks>
    /// This setting is used by SQLite-based providers.
    /// Default: <see langword="true"/>.
    /// </remarks>
    public bool EnableWalMode { get; set; } = true;

    /// <summary>
    /// Gets or sets the JSON serializer options for event serialization.
    /// </summary>
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }
}
