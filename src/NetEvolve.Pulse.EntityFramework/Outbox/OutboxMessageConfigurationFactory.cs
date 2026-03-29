namespace NetEvolve.Pulse.Outbox;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetEvolve.Pulse.Configurations;
using NetEvolve.Pulse.Extensibility.Outbox;

/// <summary>
/// Factory for creating provider-appropriate <see cref="OutboxMessageConfigurationBase"/> instances.
/// Inspects the active EF Core provider and returns the matching configuration class.
/// </summary>
/// <remarks>
/// <para><strong>Supported providers:</strong></para>
/// <list type="table">
/// <listheader>
///   <term>Provider name</term>
///   <description>Configuration type</description>
/// </listheader>
/// <item>
///   <term><c>Npgsql.EntityFrameworkCore.PostgreSQL</c></term>
///   <description><see cref="PostgreSqlOutboxMessageConfiguration"/></description>
/// </item>
/// <item>
///   <term><c>Microsoft.EntityFrameworkCore.Sqlite</c></term>
///   <description><see cref="SqliteOutboxMessageConfiguration"/></description>
/// </item>
/// <item>
///   <term><c>Pomelo.EntityFrameworkCore.MySql</c></term>
///   <description><see cref="MySqlOutboxMessageConfiguration"/></description>
/// </item>
/// <item>
///   <term><c>MySql.EntityFrameworkCore</c></term>
///   <description><see cref="MySqlOutboxMessageConfiguration"/></description>
/// </item>
/// <item>
///   <term><c>Microsoft.EntityFrameworkCore.SqlServer</c></term>
///   <description><see cref="SqlServerOutboxMessageConfiguration"/></description>
/// </item>
/// </list>
/// <para><strong>Recommended usage:</strong></para>
/// Call <see cref="Create(DbContext, IOptions{OutboxOptions})"/> inside
/// <c>OnModelCreating</c> where the <see cref="DbContext"/> is available. This avoids
/// hard-coding a provider-specific configuration class in the DbContext.
/// </remarks>
/// <example>
/// <code>
/// protected override void OnModelCreating(ModelBuilder modelBuilder)
/// {
///     base.OnModelCreating(modelBuilder);
///
///     // Automatically selects the right configuration for the active provider
///     modelBuilder.ApplyConfiguration(
///         OutboxMessageConfigurationFactory.Create(this));
///
///     // With custom options
///     modelBuilder.ApplyConfiguration(
///         OutboxMessageConfigurationFactory.Create(this,
///             Options.Create(new OutboxOptions { Schema = "myschema" })));
/// }
/// </code>
/// </example>
public static class OutboxMessageConfigurationFactory
{
    /// <summary>
    /// The provider name for Npgsql (PostgreSQL).
    /// </summary>
    private const string NpgsqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    /// <summary>
    /// The provider name for Microsoft.EntityFrameworkCore.Sqlite.
    /// </summary>
    private const string SqliteProviderName = "Microsoft.EntityFrameworkCore.Sqlite";

    /// <summary>
    /// The provider name for Microsoft.EntityFrameworkCore.SqlServer.
    /// </summary>
    private const string SqlServerProviderName = "Microsoft.EntityFrameworkCore.SqlServer";

    /// <summary>
    /// The provider name for Pomelo MySQL (<c>Pomelo.EntityFrameworkCore.MySql</c>).
    /// </summary>
    private const string PomeloMySqlProviderName = "Pomelo.EntityFrameworkCore.MySql";

    /// <summary>
    /// The provider name for the Oracle MySQL provider (<c>MySql.EntityFrameworkCore</c>).
    /// </summary>
    private const string OracleMySqlProviderName = "MySql.EntityFrameworkCore";

    /// <summary>
    /// Creates the appropriate <see cref="IEntityTypeConfiguration{TEntity}"/> by reading the
    /// provider name from <paramref name="context"/>.
    /// </summary>
    /// <param name="context">The <see cref="DbContext"/> whose provider determines the configuration type.</param>
    /// <param name="options">Optional outbox options. Defaults to <see cref="OutboxOptions"/> with its defaults.</param>
    /// <returns>A provider-specific <see cref="IEntityTypeConfiguration{TEntity}"/> for <see cref="OutboxMessage"/>.</returns>
    public static IEntityTypeConfiguration<OutboxMessage> Create(
        DbContext context,
        IOptions<OutboxOptions>? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        return Create(context.Database.ProviderName, options);
    }

    /// <summary>
    /// Creates the appropriate <see cref="IEntityTypeConfiguration{TEntity}"/> for the given
    /// EF Core provider name.
    /// </summary>
    /// <param name="providerName">
    /// The EF Core provider name (e.g. <see cref="NpgsqlProviderName"/>, <see cref="SqliteProviderName"/>).
    /// Pass <see langword="null"/> to get the SQL Server default.
    /// </param>
    /// <param name="options">Optional outbox options. Defaults to <see cref="OutboxOptions"/> with its defaults.</param>
    /// <returns>A provider-specific <see cref="IEntityTypeConfiguration{TEntity}"/> for <see cref="OutboxMessage"/>.</returns>
    /// <exception cref="NotSupportedException">Thrown when the specified provider is not supported.</exception>
    public static IEntityTypeConfiguration<OutboxMessage> Create(
        string? providerName,
        IOptions<OutboxOptions>? options = null
    )
    {
        var resolvedOptions = options ?? Options.Create(new OutboxOptions());
        return providerName switch
        {
            NpgsqlProviderName => new PostgreSqlOutboxMessageConfiguration(resolvedOptions),
            SqliteProviderName => new SqliteOutboxMessageConfiguration(resolvedOptions),
            SqlServerProviderName => new SqlServerOutboxMessageConfiguration(resolvedOptions),
            PomeloMySqlProviderName or OracleMySqlProviderName => new MySqlOutboxMessageConfiguration(resolvedOptions),
            _ => throw new NotSupportedException($"Unsupported EF Core provider: {providerName}"),
        };
    }
}
