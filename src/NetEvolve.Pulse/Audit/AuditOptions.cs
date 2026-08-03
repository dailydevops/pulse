namespace NetEvolve.Pulse.Audit;

using System;
using System.Collections.Generic;

/// <summary>
/// Configuration options for the audit trail.
/// </summary>
public sealed class AuditOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the serialized request payload is stored
    /// on the recorded <c>AuditRecord</c>.
    /// </summary>
    /// <remarks>
    /// When enabled, the payload is serialized via the registered <c>IPayloadSerializer</c>
    /// by the audit request interceptor. Default: <see langword="false"/>.
    /// </remarks>
    public bool CapturePayload { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether queries, in addition to commands, are recorded
    /// to the audit trail.
    /// </summary>
    /// <remarks>
    /// Default: <see langword="false"/>, meaning only commands are audited.
    /// </remarks>
    public bool AuditQueries { get; set; }

    /// <summary>
    /// Gets the set of request types that are excluded from auditing entirely.
    /// </summary>
    public ISet<Type> ExcludedCommandTypes { get; } = new HashSet<Type>();
}
