namespace NetEvolve.Pulse.Audit;

using NetEvolve.Pulse.Extensibility.Audit;

/// <summary>
/// Configuration options for the audit trail store.
/// </summary>
public sealed class AuditStoreOptions
{
    /// <summary>
    /// Gets or sets the connection string used by the audit trail store provider.
    /// </summary>
    /// <remarks>
    /// Required for database-backed providers. Leave <see langword="null"/> when the provider
    /// obtains its connection through other means (e.g., a registered <c>DbContext</c>).
    /// </remarks>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the database schema name for the audit entry table.
    /// Default: <c>"pulse"</c>.
    /// </summary>
    /// <remarks>
    /// Set to <c>null</c> or empty string to use the default schema (e.g., "dbo" for SQL Server).
    /// Some database providers may not support schemas.
    /// </remarks>
    public string? Schema { get; set; } = AuditEntrySchema.DefaultSchema;

    /// <summary>
    /// Gets or sets the table name for audit entries.
    /// Default: <c>"AuditEntry"</c>.
    /// </summary>
    public string TableName { get; set; } = AuditEntrySchema.DefaultTableName;

    /// <summary>
    /// Gets or sets a value indicating whether WAL (Write-Ahead Logging) mode is enabled.
    /// </summary>
    /// <remarks>
    /// This setting is used by SQLite-based providers.
    /// Default: <see langword="true"/>.
    /// </remarks>
    public bool EnableWalMode { get; set; } = true;
}
