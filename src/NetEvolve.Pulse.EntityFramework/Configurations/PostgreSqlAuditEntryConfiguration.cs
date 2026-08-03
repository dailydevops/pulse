namespace NetEvolve.Pulse.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Audit;
using NetEvolve.Pulse.Extensibility.Audit;

/// <summary>
/// Entity Framework Core configuration for <see cref="AuditRecord"/> targeting PostgreSQL.
/// Uses native PostgreSQL column types for optimal compatibility.
/// </summary>
internal sealed class PostgreSqlAuditEntryConfiguration : AuditEntryConfigurationBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlAuditEntryConfiguration"/> class with default options.
    /// </summary>
    public PostgreSqlAuditEntryConfiguration()
        : this(Options.Create(new AuditStoreOptions())) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlAuditEntryConfiguration"/> class.
    /// </summary>
    /// <param name="options">The audit store options containing schema and table configuration.</param>
    public PostgreSqlAuditEntryConfiguration(IOptions<AuditStoreOptions> options)
        : base(options) { }

    /// <inheritdoc />
    protected override void ApplyColumnTypes(EntityTypeBuilder<AuditRecord> builder)
    {
        _ = builder.Property(e => e.CommandType).HasColumnType("character varying(500)");
        _ = builder.Property(e => e.UserId).HasColumnType("character varying(256)");
        _ = builder.Property(e => e.CorrelationId).HasColumnType("character varying(100)");
        _ = builder.Property(e => e.Payload).HasColumnType("text");
        _ = builder.Property(e => e.ExceptionMessage).HasColumnType("text");
        // "timestamp with time zone" (timestamptz) preserves UTC correctly.
        _ = builder.Property(e => e.OccurredAt).HasColumnType("timestamp with time zone");
    }
}
