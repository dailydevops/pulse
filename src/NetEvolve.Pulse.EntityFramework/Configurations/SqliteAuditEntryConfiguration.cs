namespace NetEvolve.Pulse.Configurations;

using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Audit;
using NetEvolve.Pulse.Extensibility.Audit;

/// <summary>
/// Entity Framework Core configuration for <see cref="AuditRecord"/> targeting SQLite.
/// Uses native SQLite storage classes for optimal compatibility.
/// </summary>
/// <remarks>
/// SQLite does not support named schemas. If <see cref="AuditStoreOptions.Schema"/> is set,
/// it will be passed to EF Core which silently ignores it for SQLite.
/// </remarks>
internal sealed class SqliteAuditEntryConfiguration : AuditEntryConfigurationBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteAuditEntryConfiguration"/> class with default options.
    /// </summary>
    public SqliteAuditEntryConfiguration()
        : this(Options.Create(new AuditStoreOptions())) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteAuditEntryConfiguration"/> class.
    /// </summary>
    /// <param name="options">The audit store options containing schema and table configuration.</param>
    public SqliteAuditEntryConfiguration(IOptions<AuditStoreOptions> options)
        : base(options) { }

    /// <inheritdoc />
    protected override void ApplyColumnTypes(EntityTypeBuilder<AuditRecord> builder)
    {
        _ = builder.Property(e => e.CommandType).HasColumnType("TEXT");
        _ = builder.Property(e => e.UserId).HasColumnType("TEXT");
        _ = builder.Property(e => e.CorrelationId).HasColumnType("TEXT");
        _ = builder.Property(e => e.Payload).HasColumnType("TEXT");
        _ = builder.Property(e => e.ExceptionMessage).HasColumnType("TEXT");
        // DateTimeOffset stored as INTEGER (UTC ticks) for correct ordering in SQLite.
        _ = builder
            .Property(e => e.OccurredAt)
            .HasColumnType("INTEGER")
            .HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
    }
}
