namespace NetEvolve.Pulse.Extensibility.Audit;

/// <summary>
/// Defines the contract for querying audit trail records.
/// </summary>
public interface IAuditManagement
{
    /// <summary>
    /// Retrieves the audit records matching the given <paramref name="filter"/>.
    /// </summary>
    /// <param name="filter">The filter conditions to apply.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// A read-only list of audit records matching all non-<see langword="null"/> conditions
    /// of <paramref name="filter"/> (AND-combined), ordered by <see cref="AuditRecord.OccurredAt"/>
    /// descending (most recent first) and then by <see cref="AuditRecord.Id"/> descending in the
    /// store's native identifier ordering as a deterministic tie-breaker, so that pagination is stable
    /// for records sharing the same timestamp, with <see cref="AuditFilter.Skip"/> and
    /// <see cref="AuditFilter.Take"/> applied for pagination.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="filter"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="AuditFilter.Take"/> is less than or equal to zero, or <see cref="AuditFilter.Skip"/> is negative.
    /// </exception>
    Task<IReadOnlyList<AuditRecord>> QueryAsync(AuditFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single audit record by its identifier.
    /// </summary>
    /// <param name="id">The <see cref="AuditRecord.Id"/> of the record to retrieve.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// The audit record with the given <paramref name="id"/>, or <see langword="null"/> when no
    /// such record exists.
    /// </returns>
    Task<AuditRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves aggregate counts of audit records per <see cref="AuditResult"/>.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// An <see cref="AuditStatistics"/> instance describing the aggregate counts across
    /// all audit records.
    /// </returns>
    Task<AuditStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
}
