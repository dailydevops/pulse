namespace NetEvolve.Pulse.Audit;

using System;
using Microsoft.AspNetCore.Http;
using NetEvolve.Pulse.Extensibility.Audit;

/// <summary>
/// An <see cref="IAuditUserAccessor"/> implementation that reads the current authenticated user's
/// name from the ambient <see cref="HttpContext"/>.
/// </summary>
/// <remarks>
/// <para><strong>Requirements:</strong></para>
/// Requires ASP.NET Core hosting and an <see cref="IHttpContextAccessor"/> registered in the DI
/// container. Register this accessor via <c>AddHttpContextAuditUserAccessor()</c>.
/// </remarks>
internal sealed class HttpContextAuditUserAccessor : IAuditUserAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpContextAuditUserAccessor"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">The accessor used to obtain the ambient <see cref="HttpContext"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="httpContextAccessor"/> is <see langword="null"/>.</exception>
    public HttpContextAuditUserAccessor(IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);

        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc/>
    public string? GetCurrentUser() => _httpContextAccessor.HttpContext?.User?.Identity?.Name;
}
