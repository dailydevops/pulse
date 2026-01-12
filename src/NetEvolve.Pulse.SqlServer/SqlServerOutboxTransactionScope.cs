namespace NetEvolve.Pulse;

using Microsoft.Data.SqlClient;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Implementation of <see cref="IOutboxTransactionScope"/> for SQL Server transactions.
/// Wraps a <see cref="SqlTransaction"/> for use with the outbox pattern.
/// </summary>
/// <remarks>
/// <para><strong>Usage:</strong></para>
/// Use this class when you want to enlist outbox operations in an existing SQL Server transaction.
/// <para><strong>Lifetime:</strong></para>
/// The lifetime of this scope should match the transaction lifetime. Dispose of this scope
/// after committing or rolling back the transaction.
/// </remarks>
/// <example>
/// <code>
/// await using var connection = new SqlConnection(connectionString);
/// await connection.OpenAsync();
/// await using var transaction = connection.BeginTransaction();
///
/// var transactionScope = new SqlServerOutboxTransactionScope(transaction);
///
/// // Business operations...
/// await outbox.StoreAsync(myEvent, cancellationToken);
///
/// await transaction.CommitAsync();
/// </code>
/// </example>
public sealed class SqlServerOutboxTransactionScope : IOutboxTransactionScope
{
    private readonly SqlTransaction? _transaction;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerOutboxTransactionScope"/> class.
    /// </summary>
    /// <param name="transaction">The SQL Server transaction to wrap, or null if no transaction is active.</param>
    public SqlServerOutboxTransactionScope(SqlTransaction? transaction = null) => _transaction = transaction;

    /// <inheritdoc />
    public object? GetCurrentTransaction() => _transaction;
}
