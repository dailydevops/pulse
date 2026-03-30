namespace NetEvolve.Pulse.Outbox;

using System.Text.Json;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Configuration options for the SQLite outbox pattern implementation.
/// </summary>
public sealed class SQLiteOutboxOptions
{
    /// <summary>
    /// Gets or sets the SQLite connection string.
    /// </summary>
    /// <remarks>
    /// Use <c>Data Source=:memory:</c> for in-memory databases (useful for testing).
    /// Use <c>Data Source=outbox.db</c> for file-based databases.
    /// </remarks>
    /// <example>
    /// <code>
    /// opts.ConnectionString = "Data Source=outbox.db";
    /// </code>
    /// </example>
    public string ConnectionString { get; set; } = "Data Source=outbox.db";

    /// <summary>
    /// Gets or sets the table name for outbox messages.
    /// Default: "OutboxMessage".
    /// </summary>
    public string TableName { get; set; } = OutboxMessageSchema.DefaultTableName;

    /// <summary>
    /// Gets or sets a value indicating whether WAL (Write-Ahead Logging) journal mode is enabled.
    /// Default: <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// WAL mode improves concurrent read access during write operations.
    /// Set to <see langword="false"/> when using in-memory databases (<c>Data Source=:memory:</c>),
    /// as WAL mode is not supported for in-memory databases.
    /// </remarks>
    public bool EnableWalMode { get; set; } = true;

    /// <summary>
    /// Gets or sets the JSON serializer options for event serialization.
    /// </summary>
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }
}
