namespace NetEvolve.Pulse;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Audit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Audit;

/// <summary>
/// Extension methods for configuring PostgreSQL audit trail persistence on <see cref="IMediatorBuilder"/>.
/// </summary>
public static class PostgreSqlAuditMediatorBuilderExtensions
{
    /// <summary>
    /// Adds PostgreSQL audit trail persistence using ADO.NET.
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
    /// <item><description><see cref="IAuditStore"/> as <see cref="PostgreSqlAuditStore"/> (Scoped)</description></item>
    /// <item><description><see cref="IAuditManagement"/> as <see cref="PostgreSqlAuditManagement"/> (Scoped)</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config => config
    ///     .AddPostgreSqlAuditStore(opts =>
    ///     {
    ///         opts.ConnectionString = "Host=localhost;Database=MyDb;Username=postgres;Password=secret;";
    ///         opts.Schema = "myschema";
    ///     })
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddPostgreSqlAuditStore(
        this IMediatorBuilder configurator,
        Action<AuditStoreOptions> configureOptions
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(configureOptions);

        _ = configurator.Services.Configure(configureOptions);

        return configurator.RegisterPostgreSqlAuditStore();
    }

    private static IMediatorBuilder RegisterPostgreSqlAuditStore(this IMediatorBuilder configurator)
    {
        var services = configurator.Services;

        _ = services.RemoveAll<IAuditStore>().AddScoped<IAuditStore, PostgreSqlAuditStore>();

        _ = services.RemoveAll<IAuditManagement>().AddScoped<IAuditManagement, PostgreSqlAuditManagement>();

        return configurator;
    }
}
