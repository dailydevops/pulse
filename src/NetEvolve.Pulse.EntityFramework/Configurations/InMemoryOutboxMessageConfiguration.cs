namespace NetEvolve.Pulse.Configurations;

using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;

/// <summary>
/// Entity Framework Core configuration for <see cref="OutboxMessage"/> targeting the
/// <c>Microsoft.EntityFrameworkCore.InMemory</c> provider.
/// </summary>
/// <remarks>
/// <para><strong>Provider Characteristics:</strong></para>
/// The InMemory provider is intended for testing and does not support relational concepts
/// such as column types, schemas, or filtered indexes. This configuration applies only the
/// shared structural mapping from <see cref="OutboxMessageConfigurationBase"/> — relational
/// overrides (column types, index filters) are intentionally omitted because the InMemory
/// provider silently ignores them.
/// <para><strong>Usage:</strong></para>
/// Either instantiate this class directly or use
/// <see cref="OutboxMessageConfigurationFactory.Create(string,IOptions{OutboxOptions})"/>
/// with the InMemory provider name. The factory automatically selects this configuration
/// when it detects <c>Microsoft.EntityFrameworkCore.InMemory</c> as the active provider.
/// </remarks>
/// <example>
/// <code>
/// protected override void OnModelCreating(ModelBuilder modelBuilder)
/// {
///     // Via factory (recommended — works for all providers including InMemory)
///     modelBuilder.ApplyConfiguration(
///         OutboxMessageConfigurationFactory.Create(this));
/// }
/// </code>
/// </example>
internal sealed class InMemoryOutboxMessageConfiguration : OutboxMessageConfigurationBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryOutboxMessageConfiguration"/> class
    /// with default options.
    /// </summary>
    public InMemoryOutboxMessageConfiguration()
        : this(Options.Create(new OutboxOptions())) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryOutboxMessageConfiguration"/> class.
    /// </summary>
    /// <param name="options">The outbox options containing schema and table configuration.</param>
    public InMemoryOutboxMessageConfiguration(IOptions<OutboxOptions> options)
        : base(options) { }
}
