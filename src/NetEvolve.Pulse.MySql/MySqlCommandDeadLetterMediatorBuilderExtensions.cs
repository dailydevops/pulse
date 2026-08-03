namespace NetEvolve.Pulse;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetEvolve.Pulse.DeadLetter;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.DeadLetter;

/// <summary>
/// Extension methods for configuring the MySQL command dead letter store on <see cref="IMediatorBuilder"/>.
/// </summary>
public static class MySqlCommandDeadLetterMediatorBuilderExtensions
{
    /// <summary>
    /// Adds MySQL command dead letter persistence using ADO.NET.
    /// </summary>
    /// <param name="configurator">The mediator configurator.</param>
    /// <param name="configureOptions">Action to configure <see cref="CommandDeadLetterOptions"/>.</param>
    /// <returns>The configurator for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurator"/> or <paramref name="configureOptions"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para><strong>Prerequisites:</strong></para>
    /// Execute the schema script from <c>Scripts/CommandDeadLetter.sql</c> against the target MySQL
    /// database to create the required table before using this provider.
    /// <para><strong>Schema:</strong></para>
    /// MySQL does not use schema namespaces. The <see cref="CommandDeadLetterOptions.Schema"/> property is
    /// ignored; tables are always created in the active database from the connection string.
    /// <para><strong>Interoperability:</strong></para>
    /// Stores <see cref="DateTimeOffset"/> values as <c>BIGINT</c> (UTC ticks), matching
    /// the Entity Framework MySQL provider schema.
    /// <para><strong>Registered Services:</strong></para>
    /// <list type="bullet">
    /// <item><description><see cref="ICommandDeadLetterStore"/> as <see cref="MySqlCommandDeadLetterStore"/> (Scoped)</description></item>
    /// <item><description><see cref="ICommandDeadLetterManagement"/> as <see cref="MySqlCommandDeadLetterManagement"/> (Scoped)</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPulse(config => config
    ///     .AddMySqlCommandDeadLetterStore(opts =>
    ///     {
    ///         opts.ConnectionString = "Server=localhost;Database=mydb;User Id=root;Password=secret;";
    ///     })
    /// );
    /// </code>
    /// </example>
    public static IMediatorBuilder AddMySqlCommandDeadLetterStore(
        this IMediatorBuilder configurator,
        Action<CommandDeadLetterOptions> configureOptions
    )
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(configureOptions);

        _ = configurator.Services.Configure(configureOptions);

        return configurator.RegisterMySqlCommandDeadLetterServices();
    }

    private static IMediatorBuilder RegisterMySqlCommandDeadLetterServices(this IMediatorBuilder configurator)
    {
        _ = configurator
            .Services.RemoveAll<ICommandDeadLetterStore>()
            .AddScoped<ICommandDeadLetterStore, MySqlCommandDeadLetterStore>()
            .RemoveAll<ICommandDeadLetterManagement>()
            .AddScoped<ICommandDeadLetterManagement, MySqlCommandDeadLetterManagement>();

        return configurator;
    }
}
