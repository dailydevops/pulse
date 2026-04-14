namespace NetEvolve.Pulse.Idempotency;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;

/// <summary>
/// Entity Framework Core configuration for <see cref="IdempotencyKey"/> targeting MySQL
/// via the Oracle provider (<c>MySql.EntityFrameworkCore</c>).
/// </summary>
/// <remarks>
/// <para><strong>Column Types:</strong></para>
/// <list type="bullet">
/// <item><description><c>varchar(500)</c> for the idempotency key</description></item>
/// <item><description><c>bigint</c> for <see cref="DateTimeOffset"/> — stored as UTC ticks via a <see langword="long"/> value converter</description></item>
/// </list>
/// <para><strong>Why bigint for DateTimeOffset:</strong></para>
/// The Oracle MySQL provider (<c>MySql.EntityFrameworkCore</c>) lacks a proper
/// <c>datetimeoffset</c> type mapping. Converting to <see langword="long"/> (UTC ticks)
/// eliminates the broken provider-specific type resolution and ensures correct ordering
/// and comparison semantics.
/// </remarks>
internal sealed class MySqlIdempotencyKeyConfiguration : IdempotencyKeyConfigurationBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlIdempotencyKeyConfiguration"/> class with default options.
    /// </summary>
    public MySqlIdempotencyKeyConfiguration()
        : this(Options.Create(new IdempotencyKeyOptions())) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlIdempotencyKeyConfiguration"/> class.
    /// </summary>
    /// <param name="options">The idempotency key options containing schema and table configuration.</param>
    public MySqlIdempotencyKeyConfiguration(IOptions<IdempotencyKeyOptions> options)
        : base(options) { }

    /// <inheritdoc />
    protected override void ApplyColumnTypes(EntityTypeBuilder<IdempotencyKey> builder)
    {
        _ = builder.Property(k => k.Key).HasColumnType("varchar(500)");

        // DateTimeOffset is stored as BIGINT (UTC ticks).
        // The Oracle MySQL provider lacks a proper DateTimeOffset type mapping for
        // parameterised operations. Converting to long eliminates the broken provider-specific
        // type resolution and ensures correct ordering and comparison semantics.
        // The read-back uses TimeSpan.Zero because the value is always persisted as UTC ticks
        // (v.UtcTicks), so the reconstructed DateTimeOffset correctly represents UTC.
        _ = builder
            .Property(k => k.CreatedAt)
            .HasColumnType("bigint")
            .HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
    }
}
