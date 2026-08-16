namespace NetEvolve.Pulse.DeadLetter;

using System;
using NetEvolve.Pulse.Extensibility.DeadLetter;

/// <summary>
/// Configuration options for the command dead letter store.
/// </summary>
public sealed class CommandDeadLetterOptions
{
    /// <summary>
    /// Gets or sets the connection string used by the command dead letter store provider.
    /// </summary>
    /// <remarks>
    /// Required for database-backed providers. Leave <see langword="null"/> when the provider
    /// obtains its connection through other means (e.g., a registered <c>DbContext</c>).
    /// </remarks>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the database schema name for the command dead letter table.
    /// Default: <c>"pulse"</c>.
    /// </summary>
    /// <remarks>
    /// Set to <see langword="null"/> or empty string to use the default schema (e.g., "dbo" for SQL Server).
    /// Some database providers may not support schemas.
    /// </remarks>
    public string? Schema { get; set; } = CommandDeadLetterSchema.DefaultSchema;

    /// <summary>
    /// Gets or sets the table name for command dead letter entries.
    /// Default: <c>"CommandDeadLetter"</c>.
    /// </summary>
    public string TableName { get; set; } = CommandDeadLetterSchema.DefaultTableName;

    /// <summary>
    /// Gets or sets a value indicating whether WAL (Write-Ahead Logging) mode is enabled.
    /// </summary>
    /// <remarks>
    /// This setting is used by SQLite-based providers.
    /// Default: <see langword="true"/>.
    /// </remarks>
    public bool EnableWalMode { get; set; } = true;
}
