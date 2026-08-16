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
    /// descending (most recent first), with <see cref="AuditFilter.Skip"/> and
    /// <see cref="AuditFilter.Take"/> applied for pagination.
    /// </returns>
    Task<IReadOnlyList<AuditRecord>> QueryAsync(AuditFilter filter, CancellationToken cancellationToken = default);

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
