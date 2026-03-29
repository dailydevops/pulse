namespace NetEvolve.Pulse.Outbox;

using NetEvolve.Pulse.Extensibility.Outbox;
using Npgsql;

/// <summary>
/// Implementation of <see cref="IOutboxTransactionScope"/> for PostgreSQL transactions.
/// Wraps a <see cref="NpgsqlTransaction"/> for use with the outbox pattern.
/// </summary>
/// <remarks>
/// <para><strong>Usage:</strong></para>
/// Use this class when you want to enlist outbox operations in an existing PostgreSQL transaction.
/// <para><strong>Lifetime:</strong></para>
/// The lifetime of this scope should match the transaction lifetime. Dispose of this scope
/// after committing or rolling back the transaction.
/// </remarks>
/// <example>
/// <code>
/// await using var connection = new NpgsqlConnection(connectionString);
/// await connection.OpenAsync();
/// await using var transaction = await connection.BeginTransactionAsync();
///
/// var transactionScope = new PostgreSqlOutboxTransactionScope(transaction);
///
/// // Business operations...
/// await outbox.StoreAsync(myEvent, cancellationToken);
///
/// await transaction.CommitAsync();
/// </code>
/// </example>
internal sealed class PostgreSqlOutboxTransactionScope : IOutboxTransactionScope
{
    private readonly NpgsqlTransaction? _transaction;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlOutboxTransactionScope"/> class.
    /// </summary>
    /// <param name="transaction">The PostgreSQL transaction to wrap, or null if no transaction is active.</param>
    public PostgreSqlOutboxTransactionScope(NpgsqlTransaction? transaction = null) => _transaction = transaction;

    /// <inheritdoc />
    public object? GetCurrentTransaction() => _transaction;
}
