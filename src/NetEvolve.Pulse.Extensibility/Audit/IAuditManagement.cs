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

/// <summary>
/// Describes the filter conditions applied by <see cref="IAuditManagement.QueryAsync"/>.
/// </summary>
public sealed class AuditFilter
{
    /// <summary>
    /// Gets or sets the request type name to filter by.
    /// </summary>
    /// <remarks>
    /// When <see langword="null"/>, records are not filtered by <see cref="AuditRecord.CommandType"/>.
    /// </remarks>
    public string? CommandType { get; set; }

    /// <summary>
    /// Gets or sets the user identifier to filter by.
    /// </summary>
    /// <remarks>
    /// When <see langword="null"/>, records are not filtered by <see cref="AuditRecord.UserId"/>.
    /// </remarks>
    public string? UserId { get; set; }

    /// <summary>
    /// Gets or sets the inclusive lower bound of <see cref="AuditRecord.OccurredAt"/> to filter by.
    /// </summary>
    /// <remarks>
    /// When <see langword="null"/>, records are not filtered by a lower bound.
    /// </remarks>
    public DateTimeOffset? From { get; set; }

    /// <summary>
    /// Gets or sets the inclusive upper bound of <see cref="AuditRecord.OccurredAt"/> to filter by.
    /// </summary>
    /// <remarks>
    /// When <see langword="null"/>, records are not filtered by an upper bound.
    /// </remarks>
    public DateTimeOffset? To { get; set; }

    /// <summary>
    /// Gets or sets the audit result to filter by.
    /// </summary>
    /// <remarks>
    /// When <see langword="null"/>, records are not filtered by <see cref="AuditRecord.Result"/>.
    /// </remarks>
    public AuditResult? Result { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of records to return. Default: <c>50</c>.
    /// </summary>
    public int Take { get; set; } = 50;

    /// <summary>
    /// Gets or sets the number of matching records to skip, for pagination. Default: <c>0</c>.
    /// </summary>
    public int Skip { get; set; }
}
