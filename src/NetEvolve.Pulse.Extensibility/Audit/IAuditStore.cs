namespace NetEvolve.Pulse.Extensibility.Audit;

/// <summary>
/// Defines the contract for persisting audit trail records.
/// </summary>
/// <remarks>
/// <para><strong>Implementation Guidelines:</strong></para>
/// Implementations MUST persist the given <see cref="AuditRecord"/> as a new row, without
/// modifying any of its property values.
/// </remarks>
public interface IAuditStore
{
    /// <summary>
    /// Stores the given audit record.
    /// </summary>
    /// <param name="record">The audit record to persist.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RecordAsync(AuditRecord record, CancellationToken cancellationToken = default);
}
