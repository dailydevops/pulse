namespace NetEvolve.Pulse.Extensibility.Audit;

/// <summary>
/// Represents the outcome of a request that was recorded to the audit trail.
/// </summary>
public enum AuditResult
{
    /// <summary>
    /// The request completed successfully.
    /// </summary>
    Success = 0,

    /// <summary>
    /// The request failed, i.e. the handler threw an exception.
    /// </summary>
    Failure = 1,
}
