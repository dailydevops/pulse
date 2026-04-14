namespace NetEvolve.Pulse.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
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
        _ = builder.Property(k => k.Key).HasColumnType("nvarchar(500)");
        _ = builder.Property(k => k.CreatedAt).HasColumnType("datetimeoffset");
    }
}
