namespace NetEvolve.Pulse.Idempotency;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// Defines the contract for a DbContext that supports idempotency key persistence.
/// Implement this interface in your application's DbContext to enable Entity Framework idempotency store support.
/// </summary>
/// <remarks>
/// <para><strong>Implementation:</strong></para>
/// Your DbContext must expose a <see cref="DbSet{TEntity}"/> for <see cref="IdempotencyKey"/>
/// and apply the appropriate <c>IdempotencyKeyConfiguration</c> in <c>OnModelCreating</c>.
/// <para><strong>Migration Workflow:</strong></para>
/// <list type="number">
/// <item><description>Implement <see cref="IIdempotencyStoreDbContext"/> in your DbContext</description></item>
/// <item><description>Apply the idempotency configuration via <c>modelBuilder.ApplyPulseConfiguration(this)</c> in OnModelCreating</description></item>
/// <item><description>Run <c>dotnet ef migrations add AddIdempotencyKeys</c> with your chosen provider</description></item>
/// <item><description>Apply migration with <c>dotnet ef database update</c></description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// public class ApplicationDbContext : DbContext, IIdempotencyStoreDbContext
/// {
///     public DbSet&lt;IdempotencyKey&gt; IdempotencyKeys =&gt; Set&lt;IdempotencyKey&gt;();
///
///     protected override void OnModelCreating(ModelBuilder modelBuilder)
///     {
///         base.OnModelCreating(modelBuilder);
///         modelBuilder.ApplyPulseConfiguration(this);
///     }
/// }
/// </code>
/// </example>
public interface IIdempotencyStoreDbContext
{
    /// <summary>
    /// Gets the <see cref="DbSet{TEntity}"/> for idempotency keys.
    /// </summary>
    DbSet<IdempotencyKey> IdempotencyKeys { get; }
}
