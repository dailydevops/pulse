namespace NetEvolve.Pulse.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Entity Framework Core configuration for <see cref="OutboxMessage"/> targeting MySQL.
/// Supports both Pomelo (<c>Pomelo.EntityFrameworkCore.MySql</c>) and the Oracle
/// (<c>MySql.EntityFrameworkCore</c>) providers.
/// </summary>
/// <remarks>
/// <para><strong>Column Types:</strong></para>
/// <list type="bullet">
/// <item><description><c>char(36)</c> for <see cref="Guid"/> (UUID string representation)</description></item>
/// <item><description><c>varchar(n)</c> for bounded strings</description></item>
/// <item><description><c>longtext</c> for unbounded strings (Payload, Error)</description></item>
/// <item><description><c>datetime(6)</c> for <see cref="DateTimeOffset"/> — Pomelo stores values as UTC</description></item>
/// </list>
/// <para><strong>Filtered Indexes:</strong></para>
/// MySQL does not support partial/filtered indexes with a <c>WHERE</c> clause.
/// All filter properties inherit <see langword="null"/> from the base class,
/// causing EF Core to emit plain (unfiltered) indexes.
/// <para><strong>Usage:</strong></para>
/// Either instantiate this class directly or use <see cref="OutboxMessageConfigurationFactory.Create(string,IOptions{OutboxOptions})"/>
/// with a MySQL provider name.
/// <para><strong>Customization:</strong></para>
/// Override schema and table names via <see cref="OutboxOptions"/> before applying this configuration.
/// </remarks>
/// <example>
/// <code>
/// protected override void OnModelCreating(ModelBuilder modelBuilder)
/// {
///     // Directly
///     modelBuilder.ApplyConfiguration(new MySqlOutboxMessageConfiguration());
///
///     // Via factory (recommended)
///     modelBuilder.ApplyConfiguration(
///         OutboxMessageConfigurationFactory.Create(this));
/// }
/// </code>
/// </example>
internal sealed class MySqlOutboxMessageConfiguration : OutboxMessageConfigurationBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlOutboxMessageConfiguration"/> class with default options.
    /// </summary>
    public MySqlOutboxMessageConfiguration()
        : this(Options.Create(new OutboxOptions())) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlOutboxMessageConfiguration"/> class.
    /// </summary>
    /// <param name="options">The outbox options containing schema and table configuration.</param>
    public MySqlOutboxMessageConfiguration(IOptions<OutboxOptions> options)
        : base(options) { }

    /// <inheritdoc />
    protected override void ApplyColumnTypes(EntityTypeBuilder<OutboxMessage> builder)
    {
        // char(36) is the canonical UUID string format used by Pomelo and Oracle MySQL provider
        _ = builder.Property(m => m.Id).HasColumnType("char(36)");
        _ = builder.Property(m => m.EventType).HasColumnType("varchar(500)");
        // longtext covers MySQL's maximum row size for arbitrarily large JSON payloads
        _ = builder.Property(m => m.Payload).HasColumnType("longtext");
        _ = builder.Property(m => m.CorrelationId).HasColumnType("varchar(100)");
        // datetime(6) stores microsecond precision; Pomelo converts DateTimeOffset to UTC
        _ = builder.Property(m => m.CreatedAt).HasColumnType("datetime(6)");
        _ = builder.Property(m => m.UpdatedAt).HasColumnType("datetime(6)");
        _ = builder.Property(m => m.ProcessedAt).HasColumnType("datetime(6)");
        _ = builder.Property(m => m.NextRetryAt).HasColumnType("datetime(6)");
        _ = builder.Property(m => m.RetryCount).HasColumnType("int");
        _ = builder.Property(m => m.Error).HasColumnType("longtext");
        _ = builder.Property(m => m.Status).HasColumnType("int");
    }
}
