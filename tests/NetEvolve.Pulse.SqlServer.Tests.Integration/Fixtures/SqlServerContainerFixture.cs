namespace NetEvolve.Pulse.SqlServer.Tests.Integration.Fixtures;

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using TUnit.Core.Interfaces;

/// <summary>
/// Provides a shared SQL Server container for SQL Server ADO.NET integration tests.
/// Uses Testcontainers to manage a real SQL Server instance in Docker.
/// </summary>
/// <remarks>
/// <para><strong>Container Lifecycle:</strong></para>
/// The container is started once per assembly and shared across all tests
/// using TUnit's <c>[ClassDataSource]</c> attribute with <c>Shared = SharedType.PerAssembly</c>.
/// <para><strong>Schema Initialization:</strong></para>
/// Provides methods to initialize the outbox schema using the provided SQL script.
/// </remarks>
public sealed partial class SqlServerContainerFixture : IAsyncInitializer, IAsyncDisposable
{
    private readonly MsSqlContainer _container;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerContainerFixture"/> class.
    /// </summary>
    public SqlServerContainerFixture() =>
        _container = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithPassword("Test@Password123!")
            .Build();

    /// <summary>
    /// Starts the SQL Server container.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitializeAsync() => await _container.StartAsync().ConfigureAwait(false);

    /// <summary>
    /// Creates a new connection string for a specific database.
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    /// <returns>A connection string for the specified database.</returns>
    public string GetConnectionString(string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = databaseName,
        };
        return builder.ConnectionString;
    }

    /// <summary>
    /// Creates a new database with the specified name.
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Database name is generated internally and not from user input."
    )]
    public async Task CreateDatabaseAsync(string databaseName, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_container.GetConnectionString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var sql = $"""
            IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE [name] = N'{databaseName}')
            BEGIN
                CREATE DATABASE [{databaseName}]
            END
            """;

        await using var command = new SqlCommand(sql, connection);
        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Drops a database with the specified name.
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Database name is generated internally and not from user input."
    )]
    public async Task DropDatabaseAsync(string databaseName, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_container.GetConnectionString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var sql = $"""
            IF EXISTS (SELECT 1 FROM sys.databases WHERE [name] = N'{databaseName}')
            BEGIN
                ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{databaseName}]
            END
            """;

        await using var command = new SqlCommand(sql, connection);
        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Initializes the outbox schema in the specified database using the SQL script.
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities - Script comes from trusted embedded resource
    public async Task InitializeSchemaAsync(string databaseName, CancellationToken cancellationToken = default)
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Scripts", "OutboxMessage.sql");
        var script = await File.ReadAllTextAsync(scriptPath, cancellationToken).ConfigureAwait(false);

        await using var connection = new SqlConnection(GetConnectionString(databaseName));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Split script on GO statements and execute each batch
        var batches = GoBatchSeparator().Split(script);

        foreach (var batch in batches)
        {
            var trimmedBatch = batch.Trim();
            if (string.IsNullOrWhiteSpace(trimmedBatch))
            {
                continue;
            }

            await using var command = new SqlCommand(trimmedBatch, connection);
            _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
#pragma warning restore CA2100

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoBatchSeparator();
}
