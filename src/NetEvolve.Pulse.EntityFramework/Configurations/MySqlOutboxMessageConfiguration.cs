namespace NetEvolve.Pulse.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Entity Framework Core configuration for <see cref="OutboxMessage"/> targeting MySQL
/// via the Oracle provider (<c>MySql.EntityFrameworkCore</c>).
/// </summary>
/// <remarks>
/// <para><strong>Column Types:</strong></para>
/// <list type="bullet">
/// <item><description><c>binary(16)</c> for <see cref="Guid"/> — raw 16-byte UUID with a <c>byte[]</c> value converter</description></item>
/// <item><description><c>varchar(n)</c> for bounded strings</description></item>
/// <item><description><c>longtext</c> for unbounded strings (Payload, Error)</description></item>
/// <item><description><c>bigint</c> for <see cref="DateTimeOffset"/> — stored as UTC ticks via a <see langword="long"/> value converter</description></item>
/// </list>
/// <para><strong>Why binary(16) and bigint:</strong></para>
/// The Oracle MySQL provider does not produce a valid type mapping for
/// <see cref="Guid"/>→<c>char(36)</c> SQL parameter binding (returns <see langword="null"/>,
/// causing a <see cref="System.NullReferenceException"/> in
/// <c>TypeMappedRelationalParameter.AddDbParameter</c>).
/// Using <c>binary(16)</c> with an explicit <c>byte[]</c> converter provides a working
/// binding. Similarly, the provider lacks a proper <see cref="DateTimeOffset"/> SQL type
/// mapping; converting to <see langword="long"/> (UTC ticks) eliminates the broken
/// provider-specific type resolution and ensures correct ordering and comparison semantics.
/// <para><strong>Filtered Indexes:</strong></para>
/// MySQL does not support partial/filtered indexes with a <c>WHERE</c> clause.
/// All filter properties inherit <see langword="null"/> from the base class,
/// causing EF Core to emit plain (unfiltered) indexes.
/// <para><strong>Usage:</strong></para>
/// Either instantiate this class directly or call
/// <see cref="ModelBuilderExtensions.ApplyPulseConfiguration{TContext}(ModelBuilder, TContext)"/>
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
///     // Via extension method (recommended)
///     modelBuilder.ApplyPulseConfiguration(this);
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
        // binary(16) stores the raw 16-byte UUID — half the storage of char(36), faster binary
        // comparisons, and better index locality. Critically, the Oracle MySQL provider
        // (MySql.EntityFrameworkCore) has a working byte[]→binary(16) type mapping for SQL
        // parameter binding, whereas Guid→char(36) returns a null TypeMapping and throws
        // NullReferenceException inside TypeMappedRelationalParameter.AddDbParameter.
        _ = builder
            .Property(m => m.Id)
            .HasColumnType("binary(16)")
            .HasConversion(v => v.ToByteArray(), v => new Guid(v));
        _ = builder.Property(m => m.EventType).HasColumnType("varchar(500)");
        // longtext covers MySQL's maximum row size for arbitrarily large JSON payloads
        _ = builder.Property(m => m.Payload).HasColumnType("longtext");
        _ = builder.Property(m => m.CorrelationId).HasColumnType("varchar(100)");

        // DateTimeOffset columns are stored as BIGINT (UTC ticks), matching the SQLite
        // approach (INTEGER / UTC ticks). The Oracle MySQL provider lacks a proper
        // DateTimeOffset type mapping for parameterised operations (ExecuteUpdateAsync,
        // IN clauses, etc.). Converting to long eliminates the broken provider-specific
        // type resolution and ensures correct ordering and comparison semantics.
        _ = builder
            .Property(m => m.CreatedAt)
            .HasColumnType("bigint")
            .HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
        _ = builder
            .Property(m => m.UpdatedAt)
            .HasColumnType("bigint")
            .HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
        _ = builder
            .Property(m => m.ProcessedAt)
            .HasColumnType("bigint")
            .HasConversion(
                v => v.HasValue ? (long?)v.Value.UtcTicks : null,
                v => v.HasValue ? (DateTimeOffset?)new DateTimeOffset(v.Value, TimeSpan.Zero) : null
            );
        _ = builder
            .Property(m => m.NextRetryAt)
            .HasColumnType("bigint")
            .HasConversion(
                v => v.HasValue ? (long?)v.Value.UtcTicks : null,
                v => v.HasValue ? (DateTimeOffset?)new DateTimeOffset(v.Value, TimeSpan.Zero) : null
            );

        _ = builder.Property(m => m.RetryCount).HasColumnType("int");
        _ = builder.Property(m => m.Error).HasColumnType("longtext");
        _ = builder.Property(m => m.Status).HasColumnType("int");
    }
}
