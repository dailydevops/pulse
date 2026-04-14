namespace NetEvolve.Pulse.Idempotency;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Extensibility.Idempotency;

/// <summary>
/// Entity Framework Core implementation of <see cref="IIdempotencyStore"/>.
/// Provides idempotency key persistence using any EF Core database provider.
/// </summary>
/// <remarks>
/// <para><strong>Provider Agnostic:</strong></para>
/// Works with any EF Core database provider (SQL Server, PostgreSQL, SQLite, etc.).
/// <para><strong>Duplicate Key Handling:</strong></para>
/// Concurrent inserts of the same key are handled gracefully — a database unique constraint
/// violation is caught and treated as a successful (idempotent) store operation.
/// <para><strong>Time-to-Live:</strong></para>
/// When <see cref="IdempotencyKeyOptions.TimeToLive"/> is set, keys older than the TTL
/// are treated as absent by <see cref="ExistsAsync"/>. Physical deletion is not performed;
/// expired keys are logically ignored.
/// </remarks>
/// <typeparam name="TContext">The DbContext type that implements <see cref="IIdempotencyStoreDbContext"/>.</typeparam>
internal sealed class EntityFrameworkIdempotencyStore<TContext> : IIdempotencyStore
    where TContext : DbContext, IIdempotencyStoreDbContext
{
    private readonly TContext _context;
    private readonly IdempotencyKeyOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityFrameworkIdempotencyStore{TContext}"/> class.
    /// </summary>
    /// <param name="context">The DbContext for database operations.</param>
    /// <param name="options">The idempotency key options.</param>
    /// <param name="timeProvider">The time provider for computing TTL cutoff timestamps.</param>
    public EntityFrameworkIdempotencyStore(
        TContext context,
        IOptions<IdempotencyKeyOptions> options,
        TimeProvider timeProvider
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _context = context;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        if (_options.TimeToLive.HasValue)
        {
            var cutoff = _timeProvider.GetUtcNow() - _options.TimeToLive.Value;
            return _context.IdempotencyKeys.AnyAsync(
                k => k.Key == idempotencyKey && k.CreatedAt >= cutoff,
                cancellationToken
            );
        }

        return _context.IdempotencyKeys.AnyAsync(k => k.Key == idempotencyKey, cancellationToken);
    }

    /// <inheritdoc />
    public async Task StoreAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        // Check the local change tracker first to avoid a duplicate-tracking exception
        // from EF Core when the same key is stored twice within the same DbContext scope.
        if (_context.IdempotencyKeys.Local.Any(k => k.Key == idempotencyKey))
        {
            return;
        }

        var entry = new IdempotencyKey { Key = idempotencyKey, CreatedAt = _timeProvider.GetUtcNow() };

        _ = await _context.IdempotencyKeys.AddAsync(entry, cancellationToken).ConfigureAwait(false);

        try
        {
            _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
        {
            // A concurrent request already stored the same key — this is idempotent and safe to ignore.
            // Detach the conflicting entry so the context remains in a clean state.
            _context.Entry(entry).State = EntityState.Detached;
        }
    }

    /// <summary>
    /// Determines whether the given <see cref="DbUpdateException"/> was caused by a
    /// unique-constraint or primary-key violation (i.e., a duplicate key).
    /// </summary>
    private static bool IsDuplicateKeyException(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        if (inner is null)
        {
            return false;
        }

        var message = inner.Message;

        // SQL Server / Azure SQL — error 2627 (PK violation) or 2601 (unique index violation)
        // PostgreSQL (Npgsql) — "23505" unique_violation
        // SQLite — "UNIQUE constraint failed"
        // MySQL / MariaDB — error 1062 "Duplicate entry"
        return message.Contains("2627", StringComparison.Ordinal)
            || message.Contains("2601", StringComparison.Ordinal)
            || message.Contains("23505", StringComparison.Ordinal)
            || message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase);
    }
}
