namespace NetEvolve.Pulse.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.DeadLetter;
using NetEvolve.Pulse.Extensibility.DeadLetter;

/// <summary>
/// Entity Framework Core configuration for <see cref="CommandDeadLetterEntry"/> targeting PostgreSQL.
/// Uses native PostgreSQL column types for optimal compatibility.
/// </summary>
internal sealed class PostgreSqlCommandDeadLetterEntryConfiguration : CommandDeadLetterEntryConfigurationBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlCommandDeadLetterEntryConfiguration"/> class with default options.
    /// </summary>
    public PostgreSqlCommandDeadLetterEntryConfiguration()
        : this(Options.Create(new CommandDeadLetterOptions())) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlCommandDeadLetterEntryConfiguration"/> class.
    /// </summary>
    /// <param name="options">The command dead letter options containing schema and table configuration.</param>
    public PostgreSqlCommandDeadLetterEntryConfiguration(IOptions<CommandDeadLetterOptions> options)
        : base(options) { }

    /// <inheritdoc />
    protected override void ApplyColumnTypes(EntityTypeBuilder<CommandDeadLetterEntry> builder)
    {
        _ = builder.Property(e => e.CommandType).HasColumnType("character varying(500)");
        _ = builder.Property(e => e.Payload).HasColumnType("text");
        _ = builder.Property(e => e.ExceptionType).HasColumnType("character varying(500)");
        _ = builder.Property(e => e.ExceptionMessage).HasColumnType("text");
        // "timestamp with time zone" (timestamptz) preserves UTC correctly.
        _ = builder.Property(e => e.OccurredAt).HasColumnType("timestamp with time zone");
    }
}
