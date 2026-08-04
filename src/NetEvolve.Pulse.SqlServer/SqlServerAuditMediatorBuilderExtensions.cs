namespace NetEvolve.Pulse;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Audit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Audit;

/// <summary>
/// Extension methods for configuring the SQL Server audit trail store on <see cref="IMediatorBuilder"/>.
/// </summary>
public static class SqlServerAuditMediatorBuilderExtensions
{
    /// <summary>
    /// Adds SQL Server audit trail persistence using ADO.NET.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configureOptions">Action to configure <see cref="AuditStoreOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> or <paramref name="configureOptions"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// Execute the schema script from <c>Scripts/AuditEntry.sql</c> to create the required
    /// database objects before using this provider.
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="IAuditStore"/> as <see cref="SqlServerAuditStore"/> (Scoped)</description></item>
    /// <item><description><see cref="IAuditManagement"/> as <see cref="SqlServerAuditManagement"/> (Scoped)</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config => config
    ///     .AddSqlServerAuditStore(opts =>
    ///     {
    ///         opts.ConnectionString = "Server=.;Database=MyDb;Integrated Security=true;";
    ///         opts.Schema = "myschema";
    ///     })
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddSqlServerAuditStore(
        this IMediatorBuilder configurator,
        Action<AuditStoreOptions> configureOptions
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(configureOptions);

        var services = configurator.Services;

        _ = services.Configure(configureOptions);

        _ = services.RemoveAll<IAuditStore>().AddScoped<IAuditStore, SqlServerAuditStore>();

        _ = services.RemoveAll<IAuditManagement>().AddScoped<IAuditManagement, SqlServerAuditManagement>();

        return configurator;
    }
}
