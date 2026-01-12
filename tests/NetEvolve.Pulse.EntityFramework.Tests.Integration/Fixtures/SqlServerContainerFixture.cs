namespace NetEvolve.Pulse.EntityFramework.Tests.Integration.Fixtures;

using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using TUnit.Core.Interfaces;

/// <summary>
/// Provides a shared SQL Server container for Entity Framework integration tests.
/// Uses Testcontainers to manage a real SQL Server instance in Docker.
/// </summary>
/// <remarks>
/// <para><strong>Container Lifecycle:</strong></para>
/// The container is started once per assembly and shared across all tests
/// using TUnit's <c>[ClassDataSource]</c> attribute with <c>Shared = SharedType.PerAssembly</c>.
/// <para><strong>Database Isolation:</strong></para>
/// Each test should use a unique database name or perform cleanup to avoid test interference.
/// </remarks>
public sealed class SqlServerContainerFixture : IAsyncInitializer, IAsyncDisposable
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
        Justification = "Database name is generated internally by tests"
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
        Justification = "Database name is generated internally by tests"
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

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);
}
