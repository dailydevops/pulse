namespace NetEvolve.Pulse.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Outbox;
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
    private const uint FnvPrime = 16777619;
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
    /// The default implementation returns <see langword="null"/>, which is suitable for databases
    /// that do not support filtered/partial indexes.
    /// </summary>
    protected virtual string? PendingMessagesFilter => null;

    /// <summary>
    /// Gets the raw SQL filter expression used for the retry-scheduled messages index.
    /// Covers <see cref="OutboxMessageStatus.Failed"/> rows with non-null <see cref="OutboxMessage.NextRetryAt"/>.
    /// Used when exponential backoff is enabled to efficiently query messages scheduled for retry.
    /// Return <see langword="null"/> when the target database does not support filtered indexes
    /// (e.g. MySQL) — EF Core will omit the filter clause entirely.
    /// The default implementation returns <see langword="null"/>, which is suitable for databases
    /// that do not support filtered/partial indexes.
    /// </summary>
    protected virtual string? RetryScheduledMessagesFilter => null;

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
    /// The default implementation returns <see langword="null"/>, which is suitable for databases
    /// that do not support filtered/partial indexes.
    /// </summary>
    protected virtual string? CompletedMessagesFilter => null;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Table configuration
        var schema = string.IsNullOrWhiteSpace(_options.Schema)
            ? OutboxMessageSchema.DefaultSchema
            : _options.Schema.Trim();
        var tableName = _options.TableName;

        _ = builder.ToTable(tableName, schema);

        // Primary key
        _ = builder.HasKey(m => m.Id).HasName(TruncateIdentifier($"PK_{schema}_{tableName}"));

        // Id column
        _ = builder.Property(m => m.Id).HasColumnName(OutboxMessageSchema.Columns.Id).ValueGeneratedNever();

        // EventType column
        _ = builder
            .Property(m => m.EventType)
            .HasColumnName(OutboxMessageSchema.Columns.EventType)
            .HasMaxLength(OutboxMessageSchema.MaxLengths.EventType)
            .HasConversion<TypeValueConverter>()
            .IsRequired();

        // Payload column (max length text)
        _ = builder.Property(m => m.Payload).HasColumnName(OutboxMessageSchema.Columns.Payload).IsRequired();

        // CorrelationId column
        _ = builder
            .Property(m => m.CorrelationId)
            .HasColumnName(OutboxMessageSchema.Columns.CorrelationId)
            .HasMaxLength(OutboxMessageSchema.MaxLengths.CorrelationId);

        // CausationId column
        _ = builder
            .Property(m => m.CausationId)
            .HasColumnName(OutboxMessageSchema.Columns.CausationId)
            .HasMaxLength(OutboxMessageSchema.MaxLengths.CausationId);

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
            .HasDatabaseName(TruncateIdentifier($"IX_{schema}_{tableName}_Status_CreatedAt"));

        // Index for retry-scheduled message polling (with exponential backoff)
        _ = builder
            .HasIndex(m => new { m.Status, m.NextRetryAt })
            .HasFilter(RetryScheduledMessagesFilter)
            .HasDatabaseName(TruncateIdentifier($"IX_{schema}_{tableName}_Status_NextRetryAt"));

        // Index for completed message cleanup
        _ = builder
            .HasIndex(m => new { m.Status, m.ProcessedAt })
            .HasFilter(CompletedMessagesFilter)
            .HasDatabaseName(TruncateIdentifier($"IX_{schema}_{tableName}_Status_ProcessedAt"));
    }

    /// <summary>
    /// Truncates a database identifier to the specified maximum length while maintaining uniqueness
    /// by appending a stable hash suffix when the identifier exceeds the limit.
    /// This is required for databases such as PostgreSQL that enforce a 63-character identifier limit.
    /// </summary>
    /// <param name="name">The full identifier name to potentially truncate.</param>
    /// <param name="maxLength">The maximum allowed identifier length. Defaults to 63 (PostgreSQL limit).</param>
    /// <returns>
    /// The original <paramref name="name"/> if it fits within <paramref name="maxLength"/>;
    /// otherwise, a truncated prefix combined with an 8-character hexadecimal hash suffix
    /// that uniquely identifies the original name.
    /// </returns>
    private static string TruncateIdentifier(string name, int maxLength = 63)
    {
        if (name.Length <= maxLength)
        {
            return name;
        }

        // Append a stable hash suffix to distinguish otherwise-identical truncated prefixes.
        // The hash is computed over the full name, so two names that share a long common prefix
        // but differ only in their suffix will produce different hashes.
        var hash = ComputeFnv1aHash(name);
        var hashSuffix = $"_{hash:x8}"; // "_" + 8 hex chars = 9 chars
        var prefixLength = maxLength - hashSuffix.Length;
        return name[..prefixLength] + hashSuffix;
    }

    /// <summary>
    /// Computes a stable 32-bit FNV-1a hash of the given string.
    /// FNV-1a is chosen for its simplicity, speed, and good distribution,
    /// making it suitable for generating short disambiguation suffixes in identifier names.
    /// </summary>
    private static uint ComputeFnv1aHash(string value)
    {
        var hash = 2166136261;
        foreach (var c in value)
        {
            hash ^= c;
            hash *= FnvPrime;
        }
        return hash;
    }
}
