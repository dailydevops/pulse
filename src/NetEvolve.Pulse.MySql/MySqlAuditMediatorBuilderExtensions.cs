namespace NetEvolve.Pulse;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Audit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Audit;

/// <summary>
/// Extension methods for configuring the MySQL audit trail store on <see cref="IMediatorBuilder"/>.
/// </summary>
public static class MySqlAuditMediatorBuilderExtensions
{
    /// <summary>
    /// Adds MySQL audit trail persistence using ADO.NET.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configureOptions">Action to configure <see cref="AuditStoreOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> or <paramref name="configureOptions"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// Execute the schema script from <c>Scripts/AuditEntry.sql</c> against the target MySQL
    /// database to create the required table before using this provider.
    /// <para><strong>Schema:</strong></para>
    /// MySQL does not use schema namespaces. The <see cref="AuditStoreOptions.Schema"/> property is
    /// ignored; tables are always created in the active database from the connection string.
    /// <para><strong>Interoperability:</strong></para>
    /// Stores <see cref="DateTimeOffset"/> values as <c>BIGINT</c> (UTC ticks), matching
    /// the Entity Framework MySQL provider schema.
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="IAuditStore"/> as <see cref="MySqlAuditStore"/> (Scoped)</description></item>
    /// <item><description><see cref="IAuditManagement"/> as <see cref="MySqlAuditManagement"/> (Scoped)</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config => config
    ///     .AddMySqlAuditStore(opts =>
    ///     {
    ///         opts.ConnectionString = "Server=localhost;Database=mydb;User Id=root;Password=secret;";
    ///     })
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddMySqlAuditStore(
        this IMediatorBuilder configurator,
        Action<AuditStoreOptions> configureOptions
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(configureOptions);

        _ = configurator.Services.Configure(configureOptions);

        return configurator.RegisterMySqlAuditServices();
    }

    private static IMediatorBuilder RegisterMySqlAuditServices(this IMediatorBuilder configurator)
    {
        _ = configurator
            .Services.RemoveAll<IAuditStore>()
            .AddScoped<IAuditStore, MySqlAuditStore>()
            .RemoveAll<IAuditManagement>()
            .AddScoped<IAuditManagement, MySqlAuditManagement>();

        return configurator;
    }
}
