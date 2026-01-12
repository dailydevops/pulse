namespace NetEvolve.Pulse;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Entity Framework Core configuration for <see cref="OutboxMessage"/>.
/// Applies the canonical schema to ensure interchangeability with other persistence providers.
/// </summary>
/// <remarks>
/// <para><strong>Schema Compatibility:</strong></para>
/// This configuration produces the same table structure as the SQL Server ADO.NET scripts,
/// allowing both providers to work with the same database.
/// <para><strong>Provider Agnostic:</strong></para>
/// Uses EF Core conventions that work across all database providers. Column types are
/// mapped using provider-specific conventions (e.g., NVARCHAR for SQL Server, TEXT for PostgreSQL).
/// <para><strong>Customization:</strong></para>
/// Override schema and table names via <see cref="OutboxOptions"/> before applying this configuration.
/// </remarks>
/// <example>
/// <code>
/// protected override void OnModelCreating(ModelBuilder modelBuilder)
/// {
///     // With default options
///     modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
///
///     // With custom options
///     modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration(
///         Options.Create(new OutboxOptions { Schema = "myschema" })));
/// }
/// </code>
/// </example>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    private readonly OutboxOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxMessageConfiguration"/> class with default options.
    /// </summary>
    public OutboxMessageConfiguration()
        : this(Microsoft.Extensions.Options.Options.Create(new OutboxOptions())) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxMessageConfiguration"/> class.
    /// </summary>
    /// <param name="options">The outbox options containing schema and table configuration.</param>
    public OutboxMessageConfiguration(IOptions<OutboxOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Table configuration
        if (!string.IsNullOrWhiteSpace(_options.Schema))
        {
            _ = builder.ToTable(_options.TableName, _options.Schema);
        }
        else
        {
            _ = builder.ToTable(_options.TableName);
        }

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

        // Indexes for efficient querying
        // Index for pending message polling
        _ = builder.HasIndex(m => new { m.Status, m.CreatedAt }).HasDatabaseName("IX_OutboxMessage_Status_CreatedAt");

        // Index for completed message cleanup
        _ = builder
            .HasIndex(m => new { m.Status, m.ProcessedAt })
            .HasDatabaseName("IX_OutboxMessage_Status_ProcessedAt");
    }
}
