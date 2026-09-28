namespace NetEvolve.Pulse.Audit;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse.Extensibility.Audit;

/// <summary>
/// Entity Framework Core implementation of <see cref="IAuditStore"/>.
/// Provides audit trail persistence using any EF Core database provider.
/// </summary>
/// <remarks>
/// <para><strong>Provider Agnostic:</strong></para>
/// Works with any EF Core database provider (SQL Server, PostgreSQL, SQLite, etc.).
/// </remarks>
/// <typeparam name="TContext">The DbContext type that implements <see cref="IAuditStoreDbContext"/>.</typeparam>
internal sealed class EntityFrameworkAuditStore<TContext> : IAuditStore
    where TContext : DbContext, IAuditStoreDbContext
{
    private readonly TContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityFrameworkAuditStore{TContext}"/> class.
    /// </summary>
    /// <param name="context">The DbContext for database operations.</param>
    public EntityFrameworkAuditStore(TContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task RecordAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(record);

        _ = await _context.AuditEntries.AddAsync(record, cancellationToken).ConfigureAwait(false);
        _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
