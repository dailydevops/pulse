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
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            idempotencyKey.Length,
            IdempotencyKeySchema.MaxLengths.IdempotencyKey
        );

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
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            idempotencyKey.Length,
            IdempotencyKeySchema.MaxLengths.IdempotencyKey
        );

        _ = await TryInsertAsync(idempotencyKey, createdAt, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// An expired key is deleted and then inserted again instead of being updated in place, because
    /// the Oracle MySQL provider cannot bind converted <see cref="DateTimeOffset"/> values in
    /// <c>ExecuteUpdateAsync</c> setters. Both steps are safe under concurrency: only one caller deletes
    /// the expired row, and the primary key lets only one caller insert the new one.
    /// </remarks>
    public async Task<bool> TryReserveAsync(
        string idempotencyKey,
        DateTimeOffset createdAt,
        DateTimeOffset? validFrom = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            idempotencyKey.Length,
            IdempotencyKeySchema.MaxLengths.IdempotencyKey
        );

        if (validFrom.HasValue)
        {
            await DeleteExpiredAsync(idempotencyKey, validFrom.Value, cancellationToken).ConfigureAwait(false);
        }

        return await TryInsertAsync(idempotencyKey, createdAt, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes the stored key when it was created before <paramref name="validFrom"/>.
    /// </summary>
    private async Task DeleteExpiredAsync(
        string idempotencyKey,
        DateTimeOffset validFrom,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tracked = _context.IdempotencyKeys.Local.FirstOrDefault(k => k.Key == idempotencyKey);
        if (tracked is not null && tracked.CreatedAt < validFrom)
        {
            _context.Entry(tracked).State = EntityState.Detached;
        }

        var expired = _context.IdempotencyKeys.Where(k => k.Key == idempotencyKey && k.CreatedAt < validFrom);

        if (_context.Database.IsRelational())
        {
            _ = await expired.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        // Non-relational providers (e.g. InMemory) do not support ExecuteDeleteAsync.
        var entry = await expired.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (entry is not null)
        {
            _ = _context.IdempotencyKeys.Remove(entry);
            _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Inserts the key unless it already exists.
    /// </summary>
    /// <returns><see langword="true"/> if the key was inserted; <see langword="false"/> if it already existed.</returns>
    private async Task<bool> TryInsertAsync(
        string idempotencyKey,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Check the local change tracker first to avoid a duplicate-tracking exception
        // from EF Core when the same key is stored twice within the same DbContext scope.
        if (_context.IdempotencyKeys.Local.Any(k => k.Key == idempotencyKey))
        {
            return false;
        }

        var entry = new IdempotencyKey { Key = idempotencyKey, CreatedAt = createdAt };

        _ = await _context.IdempotencyKeys.AddAsync(entry, cancellationToken).ConfigureAwait(false);

        try
        {
            _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (IsDuplicateKeyException(ex) && IsIdempotencyKeyConflict(ex, entry))
        {
            // A concurrent request already stored the same key — this is idempotent and safe to ignore.
            // Detach the conflicting entry so the context remains in a clean state.
            _context.Entry(entry).State = EntityState.Detached;
            return false;
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
    /// <summary>
    /// Determines whether a duplicate-key failure can be attributed to the idempotency key
    /// insert rather than to unrelated pending changes flushed by the same
    /// <c>SaveChangesAsync</c> call on the shared <see cref="DbContext"/>.
    /// </summary>
    /// <remarks>
    /// When the provider reports the failing entries on the <see cref="DbUpdateException"/>,
    /// the failure is only treated as an idempotency-key duplicate if one of those entries is
    /// the freshly added <see cref="IdempotencyKey"/>. Unique-constraint violations caused by
    /// other entities (e.g. a domain row with its own unique index) are rethrown so callers
    /// learn that their changes were not persisted. When no entries are reported (some
    /// providers omit them), the failure is attributed to the idempotency key to preserve the
    /// idempotent store semantics.
    /// </remarks>
    internal static bool IsIdempotencyKeyConflict(Exception ex, IdempotencyKey entry)
    {
        var current = ex;
        while (current is not null)
        {
            if (current is DbUpdateException { Entries.Count: > 0 } updateException)
            {
                return updateException.Entries.Any(e => ReferenceEquals(e.Entity, entry));
            }

            current = current.InnerException;
        }

        return true;
    }

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
