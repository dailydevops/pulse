namespace NetEvolve.Pulse.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Audit;
using NetEvolve.Pulse.Extensibility.Audit;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Abstract base class for Entity Framework Core configuration of <see cref="AuditRecord"/>.
/// Encapsulates all provider-agnostic column and key mappings, leaving column type overrides
/// as optional members to be implemented per database provider.
/// </summary>
/// <remarks>
/// <para><strong>Provider-specific column types:</strong></para>
/// Derived classes may override <see cref="ApplyColumnTypes"/> to add explicit
/// <c>HasColumnType</c> calls. Without overrides, EF Core convention-based defaults apply.
/// <para><strong>Customization:</strong></para>
/// Override schema and table names via <see cref="AuditStoreOptions"/> before applying this configuration.
/// </remarks>
internal abstract class AuditEntryConfigurationBase : IEntityTypeConfiguration<AuditRecord>
{
    private readonly AuditStoreOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="AuditEntryConfigurationBase"/>.
    /// </summary>
    /// <param name="options">The audit store options containing schema and table configuration.</param>
    protected AuditEntryConfigurationBase(IOptions<AuditStoreOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <summary>
    /// Applies provider-specific column type overrides to the entity mapping.
    /// Called from <see cref="Configure"/> after all shared column mappings are applied.
    /// </summary>
    /// <remarks>
    /// Override this method to call <c>HasColumnType</c> for columns whose native type
    /// differs between providers.
    /// The default implementation is a no-op.
    /// </remarks>
    /// <param name="builder">The entity type builder for <see cref="AuditRecord"/>.</param>
    protected virtual void ApplyColumnTypes(EntityTypeBuilder<AuditRecord> builder) { }

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AuditRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Table configuration
        var schema = string.IsNullOrWhiteSpace(_options.Schema)
            ? AuditEntrySchema.DefaultSchema
            : _options.Schema.Trim();
        var tableName = _options.TableName;
        SqlIdentifier.Validate(schema, nameof(_options.Schema));
        SqlIdentifier.Validate(tableName, nameof(_options.TableName));

        _ = builder.ToTable(tableName, schema);

        // Primary key
        _ = builder.HasKey(e => e.Id).HasName($"PK_{schema}_{tableName}");

        // Id column
        _ = builder.Property(e => e.Id).HasColumnName(AuditEntrySchema.Columns.Id).IsRequired();

        // CommandType column
        _ = builder
            .Property(e => e.CommandType)
            .HasColumnName(AuditEntrySchema.Columns.CommandType)
            .HasMaxLength(AuditEntrySchema.MaxLengths.CommandType)
            .IsRequired();

        // UserId column
        _ = builder
            .Property(e => e.UserId)
            .HasColumnName(AuditEntrySchema.Columns.UserId)
            .HasMaxLength(AuditEntrySchema.MaxLengths.UserId);

        // CorrelationId column
        _ = builder
            .Property(e => e.CorrelationId)
            .HasColumnName(AuditEntrySchema.Columns.CorrelationId)
            .HasMaxLength(AuditEntrySchema.MaxLengths.CorrelationId);

        // OccurredAt column
        _ = builder.Property(e => e.OccurredAt).HasColumnName(AuditEntrySchema.Columns.OccurredAt).IsRequired();

        // DurationMs column
        _ = builder.Property(e => e.DurationMs).HasColumnName(AuditEntrySchema.Columns.DurationMs).IsRequired();

        // Result column
        _ = builder.Property(e => e.Result).HasColumnName(AuditEntrySchema.Columns.Result).IsRequired();

        // Payload column
        _ = builder.Property(e => e.Payload).HasColumnName(AuditEntrySchema.Columns.Payload);

        // ExceptionMessage column
        _ = builder.Property(e => e.ExceptionMessage).HasColumnName(AuditEntrySchema.Columns.ExceptionMessage);

        // Provider-specific column type overrides
        ApplyColumnTypes(builder);
    }
}
