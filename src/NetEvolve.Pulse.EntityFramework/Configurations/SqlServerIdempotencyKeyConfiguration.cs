namespace NetEvolve.Pulse.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Idempotency;
using NetEvolve.Pulse.Idempotency;

/// <summary>
/// Entity Framework Core configuration for <see cref="IdempotencyKey"/> targeting SQL Server.
/// Applies the canonical schema to ensure interchangeability with other persistence providers.
/// </summary>
internal sealed class SqlServerIdempotencyKeyConfiguration : IdempotencyKeyConfigurationBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerIdempotencyKeyConfiguration"/> class with default options.
    /// </summary>
    public SqlServerIdempotencyKeyConfiguration()
        : this(Options.Create(new IdempotencyKeyOptions())) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerIdempotencyKeyConfiguration"/> class.
    /// </summary>
    /// <param name="options">The idempotency key options containing schema and table configuration.</param>
    public SqlServerIdempotencyKeyConfiguration(IOptions<IdempotencyKeyOptions> options)
        : base(options) { }

    /// <inheritdoc />
    protected override void ApplyColumnTypes(EntityTypeBuilder<IdempotencyKey> builder)
    {
        // 450 NVARCHAR characters take 900 bytes, the SQL Server limit for a clustered index key.
        // The binary collation compares keys by code point, so keys differing only by case stay distinct.
        _ = builder
            .Property(k => k.Key)
            .HasColumnType($"nvarchar({IdempotencyKeySchema.MaxLengths.IdempotencyKey})")
            .UseCollation("Latin1_General_100_BIN2");
        _ = builder.Property(k => k.CreatedAt).HasColumnType("datetimeoffset");
    }
}
