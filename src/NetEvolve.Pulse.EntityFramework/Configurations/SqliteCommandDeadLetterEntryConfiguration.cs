namespace NetEvolve.Pulse.Configurations;

using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.DeadLetter;
using NetEvolve.Pulse.Extensibility.DeadLetter;

/// <summary>
/// Entity Framework Core configuration for <see cref="CommandDeadLetterEntry"/> targeting SQLite.
/// Uses native SQLite storage classes for optimal compatibility.
/// </summary>
/// <remarks>
/// SQLite does not support named schemas. If <see cref="CommandDeadLetterOptions.Schema"/> is set,
/// it will be passed to EF Core which silently ignores it for SQLite.
/// </remarks>
internal sealed class SqliteCommandDeadLetterEntryConfiguration : CommandDeadLetterEntryConfigurationBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteCommandDeadLetterEntryConfiguration"/> class with default options.
    /// </summary>
    public SqliteCommandDeadLetterEntryConfiguration()
        : this(Options.Create(new CommandDeadLetterOptions())) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteCommandDeadLetterEntryConfiguration"/> class.
    /// </summary>
    /// <param name="options">The command dead letter options containing schema and table configuration.</param>
    public SqliteCommandDeadLetterEntryConfiguration(IOptions<CommandDeadLetterOptions> options)
        : base(options) { }

    /// <inheritdoc />
    protected override void ApplyColumnTypes(EntityTypeBuilder<CommandDeadLetterEntry> builder)
    {
        _ = builder.Property(e => e.CommandType).HasColumnType("TEXT");
        _ = builder.Property(e => e.Payload).HasColumnType("TEXT");
        _ = builder.Property(e => e.ExceptionType).HasColumnType("TEXT");
        _ = builder.Property(e => e.ExceptionMessage).HasColumnType("TEXT");
        // DateTimeOffset stored as INTEGER (UTC ticks) for correct ordering in SQLite.
        _ = builder
            .Property(e => e.OccurredAt)
            .HasColumnType("INTEGER")
            .HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
    }
}
