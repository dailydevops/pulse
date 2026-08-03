namespace NetEvolve.Pulse.Audit;

using NetEvolve.Pulse.Extensibility.Audit;

/// <summary>
/// A no-op <see cref="IAuditUserAccessor"/> implementation that never resolves a user.
/// </summary>
/// <remarks>
/// <para><strong>Use Case:</strong></para>
/// Registered as the default accessor by <c>AddAudit()</c> when no other
/// <see cref="IAuditUserAccessor"/> is registered, e.g. by
/// <c>NetEvolve.Pulse.AspNetCore</c>'s <c>HttpContextAuditUserAccessor</c>.
/// </remarks>
internal sealed class NullAuditUserAccessor : IAuditUserAccessor
{
    /// <inheritdoc/>
    public string? GetCurrentUser() => null;
}
