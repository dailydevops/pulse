namespace NetEvolve.Pulse;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Implementation of <see cref="IOutboxTransactionScope"/> for Entity Framework Core transactions.
/// Wraps the current <see cref="IDbContextTransaction"/> from the DbContext.
/// </summary>
/// <remarks>
/// <para><strong>Usage:</strong></para>
/// Register this class as scoped to provide ambient transaction information to the outbox components.
/// <para><strong>Transaction Access:</strong></para>
/// The transaction is retrieved from <see cref="DbContext.Database"/> on each access,
/// ensuring it reflects the current transaction state.
/// </remarks>
/// <typeparam name="TContext">The DbContext type.</typeparam>
public sealed class EntityFrameworkOutboxTransactionScope<TContext> : IOutboxTransactionScope
    where TContext : DbContext
{
    private readonly TContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityFrameworkOutboxTransactionScope{TContext}"/> class.
    /// </summary>
    /// <param name="context">The DbContext to retrieve the current transaction from.</param>
    public EntityFrameworkOutboxTransactionScope(TContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public object? GetCurrentTransaction() => _context.Database.CurrentTransaction;
}
