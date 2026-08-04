namespace NetEvolve.Pulse.Audit;

using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse.Extensibility.Audit;

/// <summary>
/// Defines the contract for a DbContext that supports audit trail persistence.
/// Implement this interface in your application's DbContext to enable Entity Framework audit trail store support.
/// </summary>
/// <remarks>
/// <para><strong>Implementation:</strong></para>
/// Your DbContext must expose a <see cref="DbSet{TEntity}"/> for <see cref="AuditRecord"/>
/// and apply the appropriate <c>AuditEntryConfiguration</c> in <c>OnModelCreating</c>.
/// <para><strong>Migration Workflow:</strong></para>
/// <list type="number">
/// <item><description>Implement <see cref="IAuditStoreDbContext"/> in your DbContext</description></item>
/// <item><description>Apply the audit entry configuration via <c>modelBuilder.ApplyPulseConfiguration(this)</c> in OnModelCreating</description></item>
/// <item><description>Run <c>dotnet ef migrations add AddAuditTrail</c> with your chosen provider</description></item>
/// <item><description>Apply migration with <c>dotnet ef database update</c></description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// public class ApplicationDbContext : DbContext, IAuditStoreDbContext
/// {
///     public DbSet&lt;AuditRecord&gt; AuditEntries =&gt; Set&lt;AuditRecord&gt;();
///
///     protected override void OnModelCreating(ModelBuilder modelBuilder)
///     {
///         base.OnModelCreating(modelBuilder);
///         modelBuilder.ApplyPulseConfiguration(this);
///     }
/// }
/// </code>
/// </example>
public interface IAuditStoreDbContext
{
    /// <summary>
    /// Gets the <see cref="DbSet{TEntity}"/> for audit entries.
    /// </summary>
    DbSet<AuditRecord> AuditEntries { get; }
}
