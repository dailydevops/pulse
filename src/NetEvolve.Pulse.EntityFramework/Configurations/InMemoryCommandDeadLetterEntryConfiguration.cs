namespace NetEvolve.Pulse.Configurations;

using Microsoft.Extensions.Options;
using NetEvolve.Pulse.DeadLetter;

/// <summary>
/// Entity Framework Core configuration for <see cref="Extensibility.DeadLetter.CommandDeadLetterEntry"/> targeting the
/// <c>Microsoft.EntityFrameworkCore.InMemory</c> provider.
/// Intended for testing only.
/// </summary>
internal sealed class InMemoryCommandDeadLetterEntryConfiguration : CommandDeadLetterEntryConfigurationBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryCommandDeadLetterEntryConfiguration"/> class
    /// with default options.
    /// </summary>
    public InMemoryCommandDeadLetterEntryConfiguration()
        : this(Options.Create(new CommandDeadLetterOptions())) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryCommandDeadLetterEntryConfiguration"/> class.
    /// </summary>
    /// <param name="options">The command dead letter options containing schema and table configuration.</param>
    public InMemoryCommandDeadLetterEntryConfiguration(IOptions<CommandDeadLetterOptions> options)
        : base(options) { }
}
