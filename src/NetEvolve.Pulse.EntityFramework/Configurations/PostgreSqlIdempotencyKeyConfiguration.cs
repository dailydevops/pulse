namespace NetEvolve.Pulse.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Idempotency;

/// <summary>
/// Entity Framework Core configuration for <see cref="IdempotencyKey"/> targeting PostgreSQL.
/// Uses native PostgreSQL column types for optimal compatibility.
/// </summary>
internal sealed class PostgreSqlIdempotencyKeyConfiguration : IdempotencyKeyConfigurationBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlIdempotencyKeyConfiguration"/> class with default options.
    /// </summary>
    public PostgreSqlIdempotencyKeyConfiguration()
        : this(Options.Create(new IdempotencyKeyOptions())) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlIdempotencyKeyConfiguration"/> class.
    /// </summary>
    /// <param name="options">The idempotency key options containing schema and table configuration.</param>
    public PostgreSqlIdempotencyKeyConfiguration(IOptions<IdempotencyKeyOptions> options)
        : base(options) { }

    /// <inheritdoc />
    protected override void ApplyColumnTypes(EntityTypeBuilder<IdempotencyKey> builder)
    {
        _ = builder.Property(k => k.Key).HasColumnType("character varying(500)");
        // "timestamp with time zone" (timestamptz) preserves UTC correctly.
        _ = builder.Property(k => k.CreatedAt).HasColumnType("timestamp with time zone");
    }
}
