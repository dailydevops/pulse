namespace NetEvolve.Pulse.Idempotency;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NetEvolve.Pulse.Extensibility.Idempotency;

/// <summary>
/// Entity Framework Core implementation of <see cref="IIdempotencyKeyRepository"/>.
/// Provides idempotency key persistence using any EF Core database provider.
/// </summary>
/// <remarks>
/// <para><strong>Provider Agnostic:</strong></para>
/// Works with any EF Core database provider (SQL Server, PostgreSQL, SQLite, etc.).
/// <para><strong>Duplicate Key Handling:</strong></para>
/// Concurrent inserts of the same key are handled gracefully — a database unique constraint
/// violation is caught and treated as a successful (idempotent) store operation.
/// </remarks>
/// <typeparam name="TContext">The DbContext type that implements <see cref="IIdempotencyStoreDbContext"/>.</typeparam>
internal sealed class EntityFrameworkIdempotencyKeyRepository<TContext> : IIdempotencyKeyRepository
    where TContext : DbContext, IIdempotencyStoreDbContext
{
    private readonly TContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityFrameworkIdempotencyKeyRepository{TContext}"/> class.
    /// </summary>
    /// <param name="context">The DbContext for database operations.</param>
    public EntityFrameworkIdempotencyKeyRepository(TContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(
        string idempotencyKey,
        DateTimeOffset? validFrom = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        if (validFrom.HasValue)
        {
            return _context.IdempotencyKeys.AnyAsync(
                k => k.Key == idempotencyKey && k.CreatedAt >= validFrom,
                cancellationToken
            );
        }

        return _context.IdempotencyKeys.AnyAsync(k => k.Key == idempotencyKey, cancellationToken);
    }

    /// <inheritdoc />
    public async Task StoreAsync(
        string idempotencyKey,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        // Check the local change tracker first to avoid a duplicate-tracking exception
        // from EF Core when the same key is stored twice within the same DbContext scope.
        if (_context.IdempotencyKeys.Local.Any(k => k.Key == idempotencyKey))
        {
            return;
        }

        var entry = new IdempotencyKey { Key = idempotencyKey, CreatedAt = createdAt };

        _ = await _context.IdempotencyKeys.AddAsync(entry, cancellationToken).ConfigureAwait(false);

        try
        {
            _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsDuplicateKeyException(ex))
        {
            // A concurrent request already stored the same key — this is idempotent and safe to ignore.
            // Detach the conflicting entry so the context remains in a clean state.
            _context.Entry(entry).State = EntityState.Detached;
        }
    }

    /// <summary>
    /// Determines whether the given exception was caused by a unique-constraint or
    /// primary-key violation (i.e., a duplicate key insert).
    /// </summary>
    /// <remarks>
    /// Handles exceptions from all supported EF Core providers:
    /// <list type="bullet">
    /// <item><description>
    /// <strong>Relational providers</strong> (SQL Server, PostgreSQL, SQLite, MySQL) —
    /// raise <see cref="DbUpdateException"/> wrapping a provider-specific database exception
    /// whose message contains a duplicate-key indicator. The full exception chain is walked
    /// because some providers wrap the root cause multiple levels deep.
    /// </description></item>
    /// <item><description>
    /// <strong>EF Core InMemory provider</strong> — raises <see cref="ArgumentException"/>
    /// with the message "An item with the same key has already been added."
    /// </description></item>
    /// </list>
    /// </remarks>
    internal static bool IsDuplicateKeyException(Exception ex)
    {
        // Walk the full exception chain so that providers that nest the root cause
        // more than one level deep (e.g. AggregateException wrappers) are handled correctly.
        var current = ex;
        while (current is not null)
        {
            // EF Core InMemory provider raises ArgumentException (not DbUpdateException) for
            // duplicate primary-key inserts across different DbContext instances.
            if (
                current is ArgumentException
                && current.Message.Contains(
                    "An item with the same key has already been added",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return true;
            }

            var message = current.Message;

            // SQL Server / Azure SQL:
            //   Error 2627 (PK violation):    "Violation of PRIMARY KEY constraint '...'. Cannot insert duplicate key ..."
            //   Error 2601 (unique-ix violation): "Cannot insert duplicate key row in object '...' with unique index '...'"
            // PostgreSQL (Npgsql):
            //   SQLSTATE 23505: "23505: duplicate key value violates unique constraint ..."
            // SQLite:
            //   Error 19: "SQLite Error 19: 'UNIQUE constraint failed: ...'"
            // MySQL / MariaDB:
            //   Error 1062: "Duplicate entry '...' for key '...'"
            if (
                message.Contains("Cannot insert duplicate key", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Violation of PRIMARY KEY constraint", StringComparison.OrdinalIgnoreCase)
                || message.Contains("23505", StringComparison.Ordinal)
                || message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase)
                || message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }

            current = current.InnerException;
        }

        return false;
    }
}
