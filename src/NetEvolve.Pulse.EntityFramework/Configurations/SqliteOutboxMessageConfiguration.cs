namespace NetEvolve.Pulse.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Entity Framework Core configuration for <see cref="OutboxMessage"/> targeting SQLite.
/// Uses double-quoted identifiers in index filter expressions as required by standard SQL syntax.
/// </summary>
/// <remarks>
/// <para><strong>Filter Syntax:</strong></para>
/// SQLite supports double-quoted identifiers in raw SQL expressions, e.g. <c>"Status"</c>,
/// which is the ANSI SQL standard for identifier quoting.
/// <para><strong>Schema Support:</strong></para>
/// SQLite does not support named schemas. If <see cref="OutboxOptions.Schema"/> is set,
/// it will be passed to EF Core which silently ignores it for SQLite.
/// <para><strong>Usage:</strong></para>
/// Either instantiate this class directly or call
/// <see cref="ModelBuilderExtensions.ApplyPulseConfiguration{TContext}(ModelBuilder, TContext)"/>
/// with the SQLite provider name.
/// <para><strong>Customization:</strong></para>
/// Override schema and table names via <see cref="OutboxOptions"/> before applying this configuration.
/// </remarks>
/// <example>
/// <code>
/// protected override void OnModelCreating(ModelBuilder modelBuilder)
/// {
///     // Directly
///     modelBuilder.ApplyConfiguration(new SqliteOutboxMessageConfiguration());
///
///     // Via extension method (recommended)
///     modelBuilder.ApplyPulseConfiguration(this);
/// }
/// </code>
/// </example>
internal sealed class SqliteOutboxMessageConfiguration : OutboxMessageConfigurationBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteOutboxMessageConfiguration"/> class with default options.
    /// </summary>
    public SqliteOutboxMessageConfiguration()
        : this(Options.Create(new OutboxOptions())) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteOutboxMessageConfiguration"/> class.
    /// </summary>
    /// <param name="options">The outbox options containing schema and table configuration.</param>
    public SqliteOutboxMessageConfiguration(IOptions<OutboxOptions> options)
        : base(options) { }

    /// <inheritdoc />
    protected override string PendingMessagesFilter =>
        $"\"{OutboxMessageSchema.Columns.Status}\" IN ({(int)OutboxMessageStatus.Pending}, {(int)OutboxMessageStatus.Failed})";

    /// <inheritdoc />
    protected override string RetryScheduledMessagesFilter =>
        $"\"{OutboxMessageSchema.Columns.Status}\" = {(int)OutboxMessageStatus.Failed} AND \"{OutboxMessageSchema.Columns.NextRetryAt}\" IS NOT NULL";

    /// <inheritdoc />
    protected override string CompletedMessagesFilter =>
        $"\"{OutboxMessageSchema.Columns.Status}\" = {(int)OutboxMessageStatus.Completed}";

    /// <inheritdoc />
    protected override void ApplyColumnTypes(EntityTypeBuilder<OutboxMessage> builder)
    {
        // SQLite has five storage classes: NULL, INTEGER, REAL, TEXT, BLOB.
        // Being explicit for every column prevents silent storage-class drift
        // on provider upgrades (e.g. Guid stored as BLOB instead of TEXT).
        _ = builder.Property(m => m.Id).HasColumnType("TEXT");
        _ = builder.Property(m => m.EventType).HasColumnType("TEXT");
        _ = builder.Property(m => m.Payload).HasColumnType("TEXT");
        _ = builder.Property(m => m.CorrelationId).HasColumnType("TEXT");

        // DateTimeOffset columns are stored as INTEGER (UTC ticks) in SQLite.
        // EF Core SQLite refuses to translate DateTimeOffset comparisons and ORDER BY
        // when the column is TEXT because ISO-8601 string ordering is incorrect for
        // values with non-UTC offsets. Storing as long (UTC ticks) allows EF Core to
        // generate correct INTEGER comparisons and orderings in SQL.
        _ = builder
            .Property(m => m.CreatedAt)
            .HasColumnType("INTEGER")
            .HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
        _ = builder
            .Property(m => m.UpdatedAt)
            .HasColumnType("INTEGER")
            .HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
        _ = builder
            .Property(m => m.ProcessedAt)
            .HasColumnType("INTEGER")
            .HasConversion(
                v => v.HasValue ? (long?)v.Value.UtcTicks : null,
                v => v.HasValue ? (DateTimeOffset?)new DateTimeOffset(v.Value, TimeSpan.Zero) : null
            );
        _ = builder
            .Property(m => m.NextRetryAt)
            .HasColumnType("INTEGER")
            .HasConversion(
                v => v.HasValue ? (long?)v.Value.UtcTicks : null,
                v => v.HasValue ? (DateTimeOffset?)new DateTimeOffset(v.Value, TimeSpan.Zero) : null
            );

        _ = builder.Property(m => m.RetryCount).HasColumnType("INTEGER");
        _ = builder.Property(m => m.Error).HasColumnType("TEXT");
        _ = builder.Property(m => m.Status).HasColumnType("INTEGER");
    }
}
