namespace NetEvolve.Pulse;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.Audit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Audit;

/// <summary>
/// Extension methods for configuring the SQLite audit trail store on <see cref="IMediatorBuilder"/>.
/// </summary>
public static class SQLiteAuditMediatorBuilderExtensions
{
    /// <summary>
    /// Adds SQLite audit trail persistence using ADO.NET with a full options configuration action.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configureOptions">Action to configure <see cref="AuditStoreOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> or <paramref name="configureOptions"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// Execute the schema script from <c>Scripts/004_CreateAuditEntryTable.sql</c> to create the required
    /// database objects before using this provider.
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="IAuditStore"/> as <see cref="SQLiteAuditStore"/> (Scoped)</description></item>
    /// <item><description><see cref="IAuditManagement"/> as <see cref="SQLiteAuditManagement"/> (Scoped)</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config => config
    ///     .AddSQLiteAuditStore(opts =>
    ///     {
    ///         opts.ConnectionString = "Data Source=audit.db";
    ///         opts.TableName = "MyAuditEntry";
    ///     })
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddSQLiteAuditStore(
        this IMediatorBuilder configurator,
        Action<AuditStoreOptions> configureOptions
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(configureOptions);

        var services = configurator.Services;

        _ = services.Configure(configureOptions);

        _ = services.RemoveAll<IAuditStore>().AddScoped<IAuditStore, SQLiteAuditStore>();

        _ = services.RemoveAll<IAuditManagement>().AddScoped<IAuditManagement, SQLiteAuditManagement>();

        return configurator;
    }
}
