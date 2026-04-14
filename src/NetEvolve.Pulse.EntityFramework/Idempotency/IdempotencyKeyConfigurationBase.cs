namespace NetEvolve.Pulse.Idempotency;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Idempotency;

/// <summary>
/// Abstract base class for Entity Framework Core configuration of <see cref="IdempotencyKey"/>.
/// Encapsulates all provider-agnostic column and key mappings, leaving column type overrides
/// as optional members to be implemented per database provider.
/// </summary>
/// <remarks>
/// <para><strong>Provider-specific column types:</strong></para>
/// Derived classes may override <see cref="ApplyColumnTypes"/> to add explicit
/// <c>HasColumnType</c> calls. Without overrides, EF Core convention-based defaults apply.
/// <para><strong>Customization:</strong></para>
/// Override schema and table names via <see cref="IdempotencyKeyOptions"/> before applying this configuration.
/// </remarks>
internal abstract class IdempotencyKeyConfigurationBase : IEntityTypeConfiguration<IdempotencyKey>
{
    private readonly IdempotencyKeyOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="IdempotencyKeyConfigurationBase"/>.
    /// </summary>
    /// <param name="options">The idempotency key options containing schema and table configuration.</param>
    protected IdempotencyKeyConfigurationBase(IOptions<IdempotencyKeyOptions> options)
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
    /// <param name="builder">The entity type builder for <see cref="IdempotencyKey"/>.</param>
    protected virtual void ApplyColumnTypes(EntityTypeBuilder<IdempotencyKey> builder) { }

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<IdempotencyKey> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Table configuration
        var schema = string.IsNullOrWhiteSpace(_options.Schema)
            ? IdempotencyKeySchema.DefaultSchema
            : _options.Schema.Trim();
        var tableName = _options.TableName;

        _ = builder.ToTable(tableName, schema);

        // Primary key
        _ = builder.HasKey(k => k.Key).HasName($"PK_{schema}_{tableName}");

        // IdempotencyKey column
        _ = builder
            .Property(k => k.Key)
            .HasColumnName(IdempotencyKeySchema.Columns.IdempotencyKey)
            .HasMaxLength(IdempotencyKeySchema.MaxLengths.IdempotencyKey)
            .IsRequired();

        // CreatedAt column
        _ = builder.Property(k => k.CreatedAt).HasColumnName(IdempotencyKeySchema.Columns.CreatedAt).IsRequired();

        // Provider-specific column type overrides
        ApplyColumnTypes(builder);
    }
}
