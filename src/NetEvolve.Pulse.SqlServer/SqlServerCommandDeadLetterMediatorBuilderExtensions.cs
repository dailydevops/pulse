namespace NetEvolve.Pulse;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.DeadLetter;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.DeadLetter;

/// <summary>
/// Extension methods for configuring the SQL Server command dead letter store on <see cref="IMediatorBuilder"/>.
/// </summary>
public static class SqlServerCommandDeadLetterMediatorBuilderExtensions
{
    /// <summary>
    /// Adds SQL Server command dead letter persistence using ADO.NET.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configureOptions">Action to configure <see cref="CommandDeadLetterOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> or <paramref name="configureOptions"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// Execute the schema script from <c>Scripts/CommandDeadLetter.sql</c> to create the required
    /// database objects before using this provider.
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="ICommandDeadLetterStore"/> as <see cref="SqlServerCommandDeadLetterStore"/> (Scoped)</description></item>
    /// <item><description><see cref="ICommandDeadLetterManagement"/> as <see cref="SqlServerCommandDeadLetterManagement"/> (Scoped)</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config => config
    ///     .AddSqlServerCommandDeadLetterStore(opts =>
    ///     {
    ///         opts.ConnectionString = "Server=.;Database=MyDb;Integrated Security=true;";
    ///         opts.Schema = "myschema";
    ///     })
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddSqlServerCommandDeadLetterStore(
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
            .AddScoped<ICommandDeadLetterStore, SqlServerCommandDeadLetterStore>();

        _ = services
            .RemoveAll<ICommandDeadLetterManagement>()
            .AddScoped<ICommandDeadLetterManagement, SqlServerCommandDeadLetterManagement>();

        return configurator;
    }
}
