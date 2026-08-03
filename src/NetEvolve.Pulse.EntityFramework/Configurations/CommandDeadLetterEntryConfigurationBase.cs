namespace NetEvolve.Pulse.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.DeadLetter;
using NetEvolve.Pulse.Extensibility.DeadLetter;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Abstract base class for Entity Framework Core configuration of <see cref="CommandDeadLetterEntry"/>.
/// Encapsulates all provider-agnostic column and key mappings, leaving column type overrides
/// as optional members to be implemented per database provider.
/// </summary>
/// <remarks>
/// <para><strong>Provider-specific column types:</strong></para>
/// Derived classes may override <see cref="ApplyColumnTypes"/> to add explicit
/// <c>HasColumnType</c> calls. Without overrides, EF Core convention-based defaults apply.
/// <para><strong>Customization:</strong></para>
/// Override schema and table names via <see cref="CommandDeadLetterOptions"/> before applying this configuration.
/// </remarks>
internal abstract class CommandDeadLetterEntryConfigurationBase : IEntityTypeConfiguration<CommandDeadLetterEntry>
{
    private readonly CommandDeadLetterOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="CommandDeadLetterEntryConfigurationBase"/>.
    /// </summary>
    /// <param name="options">The command dead letter options containing schema and table configuration.</param>
    protected CommandDeadLetterEntryConfigurationBase(IOptions<CommandDeadLetterOptions> options)
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
    /// <param name="builder">The entity type builder for <see cref="CommandDeadLetterEntry"/>.</param>
    protected virtual void ApplyColumnTypes(EntityTypeBuilder<CommandDeadLetterEntry> builder) { }

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CommandDeadLetterEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Table configuration
        var schema = string.IsNullOrWhiteSpace(_options.Schema)
            ? CommandDeadLetterSchema.DefaultSchema
            : _options.Schema.Trim();
        var tableName = _options.TableName;
        SqlIdentifier.Validate(schema, nameof(_options.Schema));
        SqlIdentifier.Validate(tableName, nameof(_options.TableName));

        _ = builder.ToTable(tableName, schema);

        // Primary key
        _ = builder.HasKey(e => e.Id).HasName($"PK_{schema}_{tableName}");

        // Id column
        _ = builder.Property(e => e.Id).HasColumnName(CommandDeadLetterSchema.Columns.Id).IsRequired();

        // CommandType column
        _ = builder
            .Property(e => e.CommandType)
            .HasColumnName(CommandDeadLetterSchema.Columns.CommandType)
            .HasMaxLength(CommandDeadLetterSchema.MaxLengths.CommandType)
            .IsRequired();

        // Payload column
        _ = builder.Property(e => e.Payload).HasColumnName(CommandDeadLetterSchema.Columns.Payload).IsRequired();

        // ExceptionType column
        _ = builder
            .Property(e => e.ExceptionType)
            .HasColumnName(CommandDeadLetterSchema.Columns.ExceptionType)
            .HasMaxLength(CommandDeadLetterSchema.MaxLengths.ExceptionType);

        // ExceptionMessage column
        _ = builder.Property(e => e.ExceptionMessage).HasColumnName(CommandDeadLetterSchema.Columns.ExceptionMessage);

        // OccurredAt column
        _ = builder.Property(e => e.OccurredAt).HasColumnName(CommandDeadLetterSchema.Columns.OccurredAt).IsRequired();

        // AttemptCount column
        _ = builder
            .Property(e => e.AttemptCount)
            .HasColumnName(CommandDeadLetterSchema.Columns.AttemptCount)
            .IsRequired();

        // Status column
        _ = builder.Property(e => e.Status).HasColumnName(CommandDeadLetterSchema.Columns.Status).IsRequired();

        // Provider-specific column type overrides
        ApplyColumnTypes(builder);
    }
}
