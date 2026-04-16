namespace NetEvolve.Pulse.Outbox;

using MySql.Data.MySqlClient;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Implementation of <see cref="IOutboxTransactionScope"/> for MySQL transactions.
/// Wraps a <see cref="MySqlTransaction"/> for use with the outbox pattern.
/// </summary>
/// <remarks>
/// <para><strong>Usage:</strong></para>
/// Use this class when you want to enlist outbox operations in an existing MySQL transaction.
/// <para><strong>Lifetime:</strong></para>
/// The lifetime of this scope should match the transaction lifetime. Dispose of this scope
/// after committing or rolling back the transaction.
/// </remarks>
/// <example>
/// <code>
/// await using var connection = new MySqlConnection(connectionString);
/// await connection.OpenAsync();
/// await using var transaction = await connection.BeginTransactionAsync();
///
/// var transactionScope = new MySqlOutboxTransactionScope(transaction);
///
/// // Business operations...
/// await outbox.StoreAsync(myEvent, cancellationToken);
///
/// await transaction.CommitAsync();
/// </code>
/// </example>
internal sealed class MySqlOutboxTransactionScope : IOutboxTransactionScope
{
    private readonly MySqlTransaction? _transaction;

    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlOutboxTransactionScope"/> class.
    /// </summary>
    /// <param name="transaction">The MySQL transaction to wrap, or null if no transaction is active.</param>
    public MySqlOutboxTransactionScope(MySqlTransaction? transaction = null) => _transaction = transaction;

    /// <inheritdoc />
    public object? GetCurrentTransaction() => _transaction;
}
