namespace NetEvolve.Pulse.Extensibility.Audit;

/// <summary>
/// Abstracts the retrieval of the current user for audit trail recording.
/// </summary>
/// <remarks>
/// Decouples the core library from any specific hosting model (ASP.NET Core, a background
/// service, a console application, etc.). Hosting integrations provide their own
/// implementation, e.g. one backed by <c>IHttpContextAccessor</c> in ASP.NET Core.
/// </remarks>
public interface IAuditUserAccessor
{
    /// <summary>
    /// Gets the identifier of the current user, or <see langword="null"/> when no user
    /// could be resolved (e.g. no active request, or the request is unauthenticated).
    /// </summary>
    /// <returns>The identifier of the current user, or <see langword="null"/>.</returns>
    string? GetCurrentUser();
}
