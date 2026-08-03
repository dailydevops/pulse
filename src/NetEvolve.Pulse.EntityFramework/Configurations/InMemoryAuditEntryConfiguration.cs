namespace NetEvolve.Pulse.Configurations;

using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Audit;

/// <summary>
/// Entity Framework Core configuration for <see cref="Extensibility.Audit.AuditRecord"/> targeting the
/// <c>Microsoft.EntityFrameworkCore.InMemory</c> provider.
/// Intended for testing only.
/// </summary>
internal sealed class InMemoryAuditEntryConfiguration : AuditEntryConfigurationBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryAuditEntryConfiguration"/> class
    /// with default options.
    /// </summary>
    public InMemoryAuditEntryConfiguration()
        : this(Options.Create(new AuditStoreOptions())) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryAuditEntryConfiguration"/> class.
    /// </summary>
    /// <param name="options">The audit store options containing schema and table configuration.</param>
    public InMemoryAuditEntryConfiguration(IOptions<AuditStoreOptions> options)
        : base(options) { }
}
