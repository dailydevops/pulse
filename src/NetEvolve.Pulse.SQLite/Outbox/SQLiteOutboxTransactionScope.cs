namespace NetEvolve.Pulse.Outbox;

using Microsoft.Data.Sqlite;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Implementation of <see cref="IOutboxTransactionScope"/> for SQLite transactions.
/// Wraps a <see cref="SqliteTransaction"/> for use with the outbox pattern.
/// </summary>
/// <remarks>
/// <para><strong>Usage:</strong></para>
/// Use this class when you want to enlist outbox operations in an existing SQLite transaction.
/// <para><strong>Lifetime:</strong></para>
/// The lifetime of this scope should match the transaction lifetime. Dispose of this scope
/// after committing or rolling back the transaction.
/// </remarks>
/// <example>
/// <code>
/// await using var connection = new SqliteConnection(connectionString);
/// await connection.OpenAsync();
/// await using var transaction = connection.BeginTransaction();
///
/// var transactionScope = new SQLiteOutboxTransactionScope(transaction);
///
/// // Business operations...
/// await outbox.StoreAsync(myEvent, cancellationToken);
///
/// await transaction.CommitAsync();
/// </code>
/// </example>
internal sealed class SQLiteOutboxTransactionScope : IOutboxTransactionScope
{
    private readonly SqliteTransaction? _transaction;

    /// <summary>
    /// Initializes a new instance of the <see cref="SQLiteOutboxTransactionScope"/> class.
    /// </summary>
    /// <param name="transaction">The SQLite transaction to wrap, or null if no transaction is active.</param>
    public SQLiteOutboxTransactionScope(SqliteTransaction? transaction = null) => _transaction = transaction;

    /// <inheritdoc />
    public object? GetCurrentTransaction() => _transaction;
}
