namespace NetEvolve.Pulse.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Audit;
using NetEvolve.Pulse.Extensibility.Audit;

/// <summary>
/// Entity Framework Core configuration for <see cref="AuditRecord"/> targeting SQL Server.
/// Applies the canonical schema to ensure interchangeability with other persistence providers.
/// </summary>
internal sealed class SqlServerAuditEntryConfiguration : AuditEntryConfigurationBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerAuditEntryConfiguration"/> class with default options.
    /// </summary>
    public SqlServerAuditEntryConfiguration()
        : this(Options.Create(new AuditStoreOptions())) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerAuditEntryConfiguration"/> class.
    /// </summary>
    /// <param name="options">The audit store options containing schema and table configuration.</param>
    public SqlServerAuditEntryConfiguration(IOptions<AuditStoreOptions> options)
        : base(options) { }

    /// <inheritdoc />
    protected override void ApplyColumnTypes(EntityTypeBuilder<AuditRecord> builder)
    {
        _ = builder.Property(e => e.CommandType).HasColumnType("nvarchar(500)");
        _ = builder.Property(e => e.UserId).HasColumnType("nvarchar(256)");
        _ = builder.Property(e => e.CorrelationId).HasColumnType("nvarchar(100)");
        _ = builder.Property(e => e.Payload).HasColumnType("nvarchar(max)");
        _ = builder.Property(e => e.ExceptionMessage).HasColumnType("nvarchar(max)");
        _ = builder.Property(e => e.OccurredAt).HasColumnType("datetimeoffset");
    }
}
