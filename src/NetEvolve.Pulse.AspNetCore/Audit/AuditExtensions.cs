namespace NetEvolve.Pulse.Audit;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Audit;

/// <summary>
/// Provides extension methods for integrating the audit trail with the ASP.NET Core hosting model.
/// </summary>
/// <seealso cref="IAuditUserAccessor"/>
public static class AuditExtensions
{
    /// <summary>
    /// Replaces the default no-op <see cref="IAuditUserAccessor"/> with one that reads the current
    /// authenticated user's name from the ambient <c>HttpContext</c>.
    /// </summary>
    /// <param name="builder">The mediator builder.</param>
    /// <returns>The builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// Must be called after <c>AddAudit()</c>, and only makes sense in an ASP.NET Core host.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(builder =>
    /// {
    ///     builder.AddAudit();
    ///     builder.AddHttpContextAuditUserAccessor();
    /// });
    /// </code>
    /// </example>
    public static IMediatorBuilder AddHttpContextAuditUserAccessor(this IMediatorBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder.Services.AddHttpContextAccessor();

        _ = builder.Services.RemoveAll<IAuditUserAccessor>();
        _ = builder.Services.AddSingleton<IAuditUserAccessor, HttpContextAuditUserAccessor>();

        return builder;
    }
}
