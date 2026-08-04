namespace NetEvolve.Pulse;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Audit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Audit;
using NetEvolve.Pulse.Interceptors;

/// <summary>
/// Provides extension methods for registering the audit trail interceptor with the Pulse mediator.
/// </summary>
/// <seealso cref="AuditOptions"/>
/// <seealso cref="IAuditStore"/>
/// <seealso cref="IAuditUserAccessor"/>
public static class AuditMediatorBuilderExtensions
{
    /// <summary>
    /// Registers the audit trail interceptor. Commands, and optionally queries, are recorded to
    /// <see cref="IAuditStore"/> as they are processed by the mediator.
    /// </summary>
    /// <param name="builder">The mediator builder.</param>
    /// <param name="configure">
    /// An optional delegate that configures <see cref="AuditOptions"/>.
    /// When <see langword="null"/>, default options are used.
    /// </param>
    /// <returns>The builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method only registers the interceptor - it does not register an <see cref="IAuditStore"/>.
    /// Callers must also register a store, e.g. via one of the provider-specific
    /// <c>Add*AuditStore()</c> extensions (EF Core or an ADO.NET provider), for audit records to actually
    /// be persisted. Without a registered store, the interceptor is a harmless no-op.
    /// <para>
    /// By default, the current user is resolved via a no-op <see cref="IAuditUserAccessor"/> that always
    /// returns <see langword="null"/>. In an ASP.NET Core host, call
    /// <c>NetEvolve.Pulse.AspNetCore</c>'s <c>AddHttpContextAuditUserAccessor()</c> to replace it with an
    /// accessor that reads the current user from the ambient <c>HttpContext</c>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(builder =>
    /// {
    ///     builder.AddAudit(options => options.CapturePayload = true);
    ///     // builder.AddSqlServerAuditStore(...); // registers the store
    /// });
    /// </code>
    /// </example>
    public static IMediatorBuilder AddAudit(this IMediatorBuilder builder, Action<AuditOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder.Services.AddOptions<AuditOptions>();

        if (configure is not null)
        {
            _ = builder.Services.Configure(configure);
        }

        builder.Services.TryAddSingleton<IAuditUserAccessor, NullAuditUserAccessor>();

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Scoped(typeof(IRequestInterceptor<,>), typeof(AuditRequestInterceptor<,>))
        );

        return builder;
    }
}
