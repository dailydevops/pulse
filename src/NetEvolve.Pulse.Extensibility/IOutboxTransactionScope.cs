namespace NetEvolve.Pulse.Extensibility;

/// <summary>
/// Defines the contract for managing transaction scope when storing outbox messages.
/// Enables integration with unit of work patterns and distributed transactions.
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong></para>
/// The outbox transaction scope allows applications using unit of work patterns or custom
/// transaction management to ensure outbox messages are stored atomically with business data.
/// <para><strong>Usage Patterns:</strong></para>
/// <list type="bullet">
/// <item><description>Entity Framework: Participate in <c>DbContext.Database.CurrentTransaction</c></description></item>
/// <item><description>ADO.NET: Enlist with existing <c>SqlTransaction</c></description></item>
/// <item><description>Unit of Work: Integrate with <c>IUnitOfWork.CommitAsync</c></description></item>
/// <item><description>TransactionScope: Automatically enlist via ambient transactions</description></item>
/// </list>
/// <para><strong>Lifetime:</strong></para>
/// Implementations are typically scoped to match the transaction lifetime.
/// </remarks>
/// <example>
/// <code>
/// public class UnitOfWork : IUnitOfWork, IOutboxTransactionScope
/// {
///     private readonly DbContext _context;
///     private IDbContextTransaction? _transaction;
///
///     public object? GetCurrentTransaction() => _transaction;
///
///     public async Task BeginTransactionAsync(CancellationToken ct)
///     {
///         _transaction = await _context.Database.BeginTransactionAsync(ct);
///     }
///
///     public async Task CommitAsync(CancellationToken ct)
///     {
///         await _context.SaveChangesAsync(ct);
///         await _transaction?.CommitAsync(ct);
///     }
/// }
/// </code>
/// </example>
public interface IOutboxTransactionScope
{
    /// <summary>
    /// Gets the current transaction object, if any.
    /// </summary>
    /// <returns>
    /// The current transaction object (e.g., <c>DbContextTransaction</c>, <c>SqlTransaction</c>),
    /// or <c>null</c> if no transaction is active.
    /// </returns>
    /// <remarks>
    /// The return type is <c>object</c> to support different transaction types without
    /// creating dependencies on specific database providers.
    /// </remarks>
    object? GetCurrentTransaction();

    /// <summary>
    /// Gets a value indicating whether a transaction is currently active.
    /// </summary>
    bool HasActiveTransaction => GetCurrentTransaction() is not null;
}
