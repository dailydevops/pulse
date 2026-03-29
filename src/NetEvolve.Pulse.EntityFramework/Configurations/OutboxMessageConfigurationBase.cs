namespace NetEvolve.Pulse.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Abstract base class for Entity Framework Core configuration of <see cref="OutboxMessage"/>.
/// Encapsulates all provider-agnostic column and key mappings, leaving index filter
/// expressions as abstract members to be implemented per database provider.
/// </summary>
/// <remarks>
/// <para><strong>Provider-specific filter expressions:</strong></para>
/// Derived classes must supply raw SQL filter strings that match the quoting conventions
/// of their target database:
/// <list type="bullet">
/// <item><description>SQL Server: bracket-quoted identifiers, e.g. <c>[Status]</c></description></item>
/// <item><description>PostgreSQL: double-quoted identifiers, e.g. <c>"Status"</c></description></item>
/// <item><description>SQLite: double-quoted identifiers, e.g. <c>"Status"</c></description></item>
/// </list>
/// <para><strong>Provider-specific column types:</strong></para>
/// Derived classes may override <see cref="ApplyColumnTypes"/> to add explicit
/// <c>HasColumnType</c> calls. Without overrides, EF Core convention-based defaults apply.
/// <para><strong>Customization:</strong></para>
/// Override schema and table names via <see cref="OutboxOptions"/> before applying this configuration.
/// </remarks>
internal abstract class OutboxMessageConfigurationBase : IEntityTypeConfiguration<OutboxMessage>
{
    private readonly OutboxOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="OutboxMessageConfigurationBase"/>.
    /// </summary>
    /// <param name="options">The outbox options containing schema and table configuration.</param>
    protected OutboxMessageConfigurationBase(IOptions<OutboxOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <summary>
    /// Gets the raw SQL filter expression used for the pending messages index.
    /// Covers <see cref="OutboxMessageStatus.Pending"/> and <see cref="OutboxMessageStatus.Failed"/> rows.
    /// Return <see langword="null"/> when the target database does not support filtered indexes
    /// (e.g. MySQL) — EF Core will omit the filter clause entirely.
    /// </summary>
    protected abstract string? PendingMessagesFilter { get; }

    /// <summary>
    /// Applies provider-specific column type overrides to the entity mapping.
    /// Called from <see cref="Configure"/> after all shared column mappings are applied.
    /// </summary>
    /// <remarks>
    /// Override this method to call <c>HasColumnType</c> for columns whose native type
    /// differs between providers (e.g. <c>uuid</c> vs. <c>uniqueidentifier</c> for <see cref="Guid"/>,
    /// or <c>timestamp with time zone</c> vs. <c>datetimeoffset</c>).
    /// The default implementation is a no-op.
    /// </remarks>
    /// <param name="builder">The entity type builder for <see cref="OutboxMessage"/>.</param>
    protected virtual void ApplyColumnTypes(EntityTypeBuilder<OutboxMessage> builder) { }

    /// <summary>
    /// Gets the raw SQL filter expression used for the completed messages index.
    /// Covers <see cref="OutboxMessageStatus.Completed"/> rows.
    /// Return <see langword="null"/> when the target database does not support filtered indexes
    /// (e.g. MySQL) — EF Core will omit the filter clause entirely.
    /// </summary>
    protected abstract string? CompletedMessagesFilter { get; }

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Table configuration
        var schema = string.IsNullOrWhiteSpace(_options.Schema)
            ? OutboxMessageSchema.DefaultSchema
            : _options.Schema.Trim();
        _ = builder.ToTable(_options.TableName, schema);

        // Primary key
        _ = builder.HasKey(m => m.Id);

        // Id column
        _ = builder.Property(m => m.Id).HasColumnName(OutboxMessageSchema.Columns.Id).ValueGeneratedNever();

        // EventType column
        _ = builder
            .Property(m => m.EventType)
            .HasColumnName(OutboxMessageSchema.Columns.EventType)
            .HasMaxLength(OutboxMessageSchema.MaxLengths.EventType)
            .IsRequired();

        // Payload column (max length text)
        _ = builder.Property(m => m.Payload).HasColumnName(OutboxMessageSchema.Columns.Payload).IsRequired();

        // CorrelationId column
        _ = builder
            .Property(m => m.CorrelationId)
            .HasColumnName(OutboxMessageSchema.Columns.CorrelationId)
            .HasMaxLength(OutboxMessageSchema.MaxLengths.CorrelationId);

        // CreatedAt column
        _ = builder.Property(m => m.CreatedAt).HasColumnName(OutboxMessageSchema.Columns.CreatedAt).IsRequired();

        // UpdatedAt column
        _ = builder.Property(m => m.UpdatedAt).HasColumnName(OutboxMessageSchema.Columns.UpdatedAt).IsRequired();

        // ProcessedAt column
        _ = builder.Property(m => m.ProcessedAt).HasColumnName(OutboxMessageSchema.Columns.ProcessedAt);

        // NextRetryAt column
        _ = builder.Property(m => m.NextRetryAt).HasColumnName(OutboxMessageSchema.Columns.NextRetryAt);

        // RetryCount column
        _ = builder
            .Property(m => m.RetryCount)
            .HasColumnName(OutboxMessageSchema.Columns.RetryCount)
            .HasDefaultValue(0)
            .IsRequired();

        // Error column
        _ = builder.Property(m => m.Error).HasColumnName(OutboxMessageSchema.Columns.Error);

        // Status column
        _ = builder
            .Property(m => m.Status)
            .HasColumnName(OutboxMessageSchema.Columns.Status)
            .HasDefaultValue(OutboxMessageStatus.Pending)
            .IsRequired();

        // Provider-specific column type overrides
        ApplyColumnTypes(builder);

        // Indexes for efficient querying
        // Index for pending message polling (Pending + Failed)
        _ = builder
            .HasIndex(m => new { m.Status, m.CreatedAt })
            .HasFilter(PendingMessagesFilter)
            .HasDatabaseName("IX_OutboxMessage_Status_CreatedAt");

        // Index for completed message cleanup
        _ = builder
            .HasIndex(m => new { m.Status, m.ProcessedAt })
            .HasFilter(CompletedMessagesFilter)
            .HasDatabaseName("IX_OutboxMessage_Status_ProcessedAt");
    }
}
