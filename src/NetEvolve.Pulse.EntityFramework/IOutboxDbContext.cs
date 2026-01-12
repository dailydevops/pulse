namespace NetEvolve.Pulse;

using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Defines the contract for a DbContext that supports outbox message persistence.
/// Implement this interface in your application's DbContext to enable Entity Framework outbox support.
/// </summary>
/// <remarks>
/// <para><strong>Implementation:</strong></para>
/// Your DbContext must expose a <see cref="DbSet{TEntity}"/> for <see cref="OutboxMessage"/>
/// and apply the <see cref="OutboxMessageConfiguration"/> in <c>OnModelCreating</c>.
/// <para><strong>Migration Workflow:</strong></para>
/// <list type="number">
/// <item><description>Implement <see cref="IOutboxDbContext"/> in your DbContext</description></item>
/// <item><description>Apply <see cref="OutboxMessageConfiguration"/> in OnModelCreating</description></item>
/// <item><description>Run <c>dotnet ef migrations add AddOutbox</c> with your chosen provider</description></item>
/// <item><description>Apply migration with <c>dotnet ef database update</c></description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// public class ApplicationDbContext : DbContext, IOutboxDbContext
/// {
///     public DbSet&lt;OutboxMessage&gt; OutboxMessages =&gt; Set&lt;OutboxMessage&gt;();
///
///     protected override void OnModelCreating(ModelBuilder modelBuilder)
///     {
///         base.OnModelCreating(modelBuilder);
///         modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
///     }
/// }
/// </code>
/// </example>
public interface IOutboxDbContext
{
    /// <summary>
    /// Gets the <see cref="DbSet{TEntity}"/> for outbox messages.
    /// </summary>
    DbSet<OutboxMessage> OutboxMessages { get; }

    /// <summary>
    /// Saves all changes made in this context to the database.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The number of state entries written to the database.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
