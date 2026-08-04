namespace NetEvolve.Pulse.Configurations;

using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Audit;
using NetEvolve.Pulse.Extensibility.Audit;

/// <summary>
/// Entity Framework Core configuration for <see cref="AuditRecord"/> targeting MySQL
/// via the Oracle provider (<c>MySql.EntityFrameworkCore</c>).
/// </summary>
/// <remarks>
/// <para><strong>Column Types:</strong></para>
/// <list type="bullet">
/// <item><description><c>varchar(500)</c> for the command type column</description></item>
/// <item><description><c>varchar(256)</c> for the user id column</description></item>
/// <item><description><c>varchar(100)</c> for the correlation id column</description></item>
/// <item><description><c>longtext</c> for the payload and exception message columns</description></item>
/// <item><description><c>bigint</c> for <see cref="DateTimeOffset"/> — stored as UTC ticks via a <see langword="long"/> value converter</description></item>
/// </list>
/// <para><strong>Why bigint for DateTimeOffset:</strong></para>
/// The Oracle MySQL provider (<c>MySql.EntityFrameworkCore</c>) lacks a proper
/// <c>datetimeoffset</c> type mapping. Converting to <see langword="long"/> (UTC ticks)
/// eliminates the broken provider-specific type resolution and ensures correct ordering
/// and comparison semantics.
/// </remarks>
internal sealed class MySqlAuditEntryConfiguration : AuditEntryConfigurationBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlAuditEntryConfiguration"/> class with default options.
    /// </summary>
    public MySqlAuditEntryConfiguration()
        : this(Options.Create(new AuditStoreOptions())) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlAuditEntryConfiguration"/> class.
    /// </summary>
    /// <param name="options">The audit store options containing schema and table configuration.</param>
    public MySqlAuditEntryConfiguration(IOptions<AuditStoreOptions> options)
        : base(options) { }

    /// <inheritdoc />
    protected override void ApplyColumnTypes(EntityTypeBuilder<AuditRecord> builder)
    {
        _ = builder.Property(e => e.CommandType).HasColumnType("varchar(500)");
        _ = builder.Property(e => e.UserId).HasColumnType("varchar(256)");
        _ = builder.Property(e => e.CorrelationId).HasColumnType("varchar(100)");
        _ = builder.Property(e => e.Payload).HasColumnType("longtext");
        _ = builder.Property(e => e.ExceptionMessage).HasColumnType("longtext");

        // DateTimeOffset is stored as BIGINT (UTC ticks).
        // The Oracle MySQL provider lacks a proper DateTimeOffset type mapping for
        // parameterised operations. Converting to long eliminates the broken provider-specific
        // type resolution and ensures correct ordering and comparison semantics.
        // The read-back uses TimeSpan.Zero because the value is always persisted as UTC ticks
        // (v.UtcTicks), so the reconstructed DateTimeOffset correctly represents UTC.
        _ = builder
            .Property(e => e.OccurredAt)
            .HasColumnType("bigint")
            .HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
    }
}
