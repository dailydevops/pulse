namespace NetEvolve.Pulse.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.DeadLetter;
using NetEvolve.Pulse.Extensibility.DeadLetter;

/// <summary>
/// Entity Framework Core configuration for <see cref="CommandDeadLetterEntry"/> targeting SQL Server.
/// Applies the canonical schema to ensure interchangeability with other persistence providers.
/// </summary>
internal sealed class SqlServerCommandDeadLetterEntryConfiguration : CommandDeadLetterEntryConfigurationBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerCommandDeadLetterEntryConfiguration"/> class with default options.
    /// </summary>
    public SqlServerCommandDeadLetterEntryConfiguration()
        : this(Options.Create(new CommandDeadLetterOptions())) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerCommandDeadLetterEntryConfiguration"/> class.
    /// </summary>
    /// <param name="options">The command dead letter options containing schema and table configuration.</param>
    public SqlServerCommandDeadLetterEntryConfiguration(IOptions<CommandDeadLetterOptions> options)
        : base(options) { }

    /// <inheritdoc />
    protected override void ApplyColumnTypes(EntityTypeBuilder<CommandDeadLetterEntry> builder)
    {
        _ = builder.Property(e => e.CommandType).HasColumnType("nvarchar(500)");
        _ = builder.Property(e => e.Payload).HasColumnType("nvarchar(max)");
        _ = builder.Property(e => e.ExceptionType).HasColumnType("nvarchar(500)");
        _ = builder.Property(e => e.ExceptionMessage).HasColumnType("nvarchar(max)");
        _ = builder.Property(e => e.OccurredAt).HasColumnType("datetimeoffset");
    }
}
