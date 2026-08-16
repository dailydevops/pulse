namespace NetEvolve.Pulse.Extensibility.Audit;

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
