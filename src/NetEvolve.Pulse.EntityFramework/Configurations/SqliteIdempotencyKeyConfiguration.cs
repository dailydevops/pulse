namespace NetEvolve.Pulse.Configurations;

using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Idempotency;

/// <summary>
/// Entity Framework Core configuration for <see cref="IdempotencyKey"/> targeting SQLite.
/// Uses native SQLite storage classes for optimal compatibility.
/// </summary>
/// <remarks>
/// SQLite does not support named schemas. If <see cref="IdempotencyKeyOptions.Schema"/> is set,
/// it will be passed to EF Core which silently ignores it for SQLite.
/// </remarks>
internal sealed class SqliteIdempotencyKeyConfiguration : IdempotencyKeyConfigurationBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteIdempotencyKeyConfiguration"/> class with default options.
    /// </summary>
    public SqliteIdempotencyKeyConfiguration()
        : this(Options.Create(new IdempotencyKeyOptions())) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteIdempotencyKeyConfiguration"/> class.
    /// </summary>
    /// <param name="options">The idempotency key options containing schema and table configuration.</param>
    public SqliteIdempotencyKeyConfiguration(IOptions<IdempotencyKeyOptions> options)
        : base(options) { }

    /// <inheritdoc />
    protected override void ApplyColumnTypes(EntityTypeBuilder<IdempotencyKey> builder)
    {
        _ = builder.Property(k => k.Key).HasColumnType("TEXT");
        // DateTimeOffset stored as INTEGER (UTC ticks) for correct ordering in SQLite.
        _ = builder
            .Property(k => k.CreatedAt)
            .HasColumnType("INTEGER")
            .HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
    }
}
