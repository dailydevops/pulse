namespace NetEvolve.Pulse.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Entity Framework Core configuration for <see cref="OutboxMessage"/> targeting SQL Server.
/// Applies the canonical schema to ensure interchangeability with other persistence providers.
/// </summary>
/// <remarks>
/// <para><strong>Schema Compatibility:</strong></para>
/// This configuration produces the same table structure as the SQL Server ADO.NET scripts,
/// allowing both providers to work with the same database.
/// <para><strong>Provider Agnostic Base:</strong></para>
/// Column mappings are defined in <see cref="OutboxMessageConfigurationBase"/>. Only the
/// index filter expressions are SQL Server-specific (bracket-quoted identifiers).
/// <para><strong>Other Providers:</strong></para>
/// Use <see cref="OutboxMessageConfigurationFactory.Create(string,Microsoft.Extensions.Options.IOptions{OutboxOptions})"/>
/// to automatically select the correct configuration for your provider, or instantiate
/// <see cref="PostgreSqlOutboxMessageConfiguration"/> or <see cref="SqliteOutboxMessageConfiguration"/> directly.
/// <para><strong>Customization:</strong></para>
/// Override schema and table names via <see cref="OutboxOptions"/> before applying this configuration.
/// </remarks>
/// <example>
/// <code>
/// protected override void OnModelCreating(ModelBuilder modelBuilder)
/// {
///     // With default options (SQL Server)
///     modelBuilder.ApplyConfiguration(new SqlServerOutboxMessageConfiguration());
///
///     // Provider-detected via factory
///     modelBuilder.ApplyConfiguration(
///         OutboxMessageConfigurationFactory.Create(this));
/// }
/// </code>
/// </example>
internal sealed class SqlServerOutboxMessageConfiguration : OutboxMessageConfigurationBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerOutboxMessageConfiguration"/> class with default options.
    /// </summary>
    public SqlServerOutboxMessageConfiguration()
        : this(Options.Create(new OutboxOptions())) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerOutboxMessageConfiguration"/> class.
    /// </summary>
    /// <param name="options">The outbox options containing schema and table configuration.</param>
    public SqlServerOutboxMessageConfiguration(IOptions<OutboxOptions> options)
        : base(options) { }

    /// <inheritdoc />
    protected override string PendingMessagesFilter =>
        $"[{OutboxMessageSchema.Columns.Status}] IN ({(int)OutboxMessageStatus.Pending}, {(int)OutboxMessageStatus.Failed})";

    /// <inheritdoc />
    protected override string CompletedMessagesFilter =>
        $"[{OutboxMessageSchema.Columns.Status}] = {(int)OutboxMessageStatus.Completed}";

    /// <inheritdoc />
    protected override void ApplyColumnTypes(EntityTypeBuilder<OutboxMessage> builder)
    {
        _ = builder.Property(m => m.Id).HasColumnType("uniqueidentifier");
        _ = builder.Property(m => m.EventType).HasColumnType("nvarchar(500)");
        _ = builder.Property(m => m.Payload).HasColumnType("nvarchar(max)");
        _ = builder.Property(m => m.CorrelationId).HasColumnType("nvarchar(100)");
        _ = builder.Property(m => m.CreatedAt).HasColumnType("datetimeoffset");
        _ = builder.Property(m => m.UpdatedAt).HasColumnType("datetimeoffset");
        _ = builder.Property(m => m.ProcessedAt).HasColumnType("datetimeoffset");
        _ = builder.Property(m => m.RetryCount).HasColumnType("int");
        _ = builder.Property(m => m.Error).HasColumnType("nvarchar(max)");
        _ = builder.Property(m => m.Status).HasColumnType("int");
    }
}
