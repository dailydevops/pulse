namespace NetEvolve.Pulse.Extensibility.Audit;

/// <summary>
/// Represents the aggregate counts of audit trail records per <see cref="AuditResult"/>.
/// This is the value returned by <see cref="IAuditManagement.GetStatisticsAsync"/>.
/// </summary>
/// <param name="SuccessCount">The number of records with <see cref="AuditResult.Success"/> result.</param>
/// <param name="FailureCount">The number of records with <see cref="AuditResult.Failure"/> result.</param>
public sealed record AuditStatistics(int SuccessCount, int FailureCount)
{
    /// <summary>
    /// Gets the total number of audit records across all results.
    /// </summary>
    public int TotalCount => SuccessCount + FailureCount;
}
