namespace NetEvolve.Pulse;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.DeadLetter;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.DeadLetter;

/// <summary>
/// Extension methods for configuring the SQLite command dead letter store on <see cref="IMediatorBuilder"/>.
/// </summary>
public static class SQLiteCommandDeadLetterMediatorBuilderExtensions
{
    /// <summary>
    /// Adds SQLite command dead letter persistence using ADO.NET with a full options configuration action.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configureOptions">Action to configure <see cref="CommandDeadLetterOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> or <paramref name="configureOptions"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// Execute the schema script from <c>Scripts/003_CreateCommandDeadLetterTable.sql</c> to create the required
    /// database objects before using this provider.
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="ICommandDeadLetterStore"/> as <see cref="SQLiteCommandDeadLetterStore"/> (Scoped)</description></item>
    /// <item><description><see cref="ICommandDeadLetterManagement"/> as <see cref="SQLiteCommandDeadLetterManagement"/> (Scoped)</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config => config
    ///     .AddSQLiteCommandDeadLetterStore(opts =>
    ///     {
    ///         opts.ConnectionString = "Data Source=deadletter.db";
    ///         opts.TableName = "MyCommandDeadLetter";
    ///     })
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddSQLiteCommandDeadLetterStore(
        this IMediatorBuilder configurator,
        Action<CommandDeadLetterOptions> configureOptions
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(configureOptions);

        var services = configurator.Services;

        _ = services.Configure(configureOptions);

        _ = services
            .RemoveAll<ICommandDeadLetterStore>()
            .AddScoped<ICommandDeadLetterStore, SQLiteCommandDeadLetterStore>();

        _ = services
            .RemoveAll<ICommandDeadLetterManagement>()
            .AddScoped<ICommandDeadLetterManagement, SQLiteCommandDeadLetterManagement>();

        return configurator;
    }
}
