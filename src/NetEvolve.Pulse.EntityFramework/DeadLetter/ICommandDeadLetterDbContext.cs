namespace NetEvolve.Pulse.DeadLetter;

using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse.Extensibility.DeadLetter;

/// <summary>
/// Defines the contract for a DbContext that supports command dead letter persistence.
/// Implement this interface in your application's DbContext to enable Entity Framework command dead letter store support.
/// </summary>
/// <remarks>
/// <para><strong>Implementation:</strong></para>
/// Your DbContext must expose a <see cref="DbSet{TEntity}"/> for <see cref="CommandDeadLetterEntry"/>
/// and apply the appropriate <c>CommandDeadLetterEntryConfiguration</c> in <c>OnModelCreating</c>.
/// <para><strong>Migration Workflow:</strong></para>
/// <list type="number">
/// <item><description>Implement <see cref="ICommandDeadLetterDbContext"/> in your DbContext</description></item>
/// <item><description>Apply the command dead letter configuration via <c>modelBuilder.ApplyPulseConfiguration(this)</c> in OnModelCreating</description></item>
/// <item><description>Run <c>dotnet ef migrations add AddCommandDeadLetter</c> with your chosen provider</description></item>
/// <item><description>Apply migration with <c>dotnet ef database update</c></description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// public class ApplicationDbContext : DbContext, ICommandDeadLetterDbContext
/// {
///     public DbSet&lt;CommandDeadLetterEntry&gt; CommandDeadLetterEntries =&gt; Set&lt;CommandDeadLetterEntry&gt;();
///
///     protected override void OnModelCreating(ModelBuilder modelBuilder)
///     {
///         base.OnModelCreating(modelBuilder);
///         modelBuilder.ApplyPulseConfiguration(this);
///     }
/// }
/// </code>
/// </example>
public interface ICommandDeadLetterDbContext
{
    /// <summary>
    /// Gets the <see cref="DbSet{TEntity}"/> for command dead letter entries.
    /// </summary>
    DbSet<CommandDeadLetterEntry> CommandDeadLetterEntries { get; }
}
