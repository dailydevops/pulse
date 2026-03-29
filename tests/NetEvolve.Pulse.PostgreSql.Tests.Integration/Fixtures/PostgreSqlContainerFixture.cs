namespace NetEvolve.Pulse.PostgreSql.Tests.Integration.Fixtures;

using System.Diagnostics.CodeAnalysis;
using Npgsql;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

/// <summary>
/// Provides a shared PostgreSQL container for PostgreSQL ADO.NET integration tests.
/// Uses Testcontainers to manage a real PostgreSQL instance in Docker.
/// </summary>
/// <remarks>
/// <para><strong>Container Lifecycle:</strong></para>
/// The container is started once per assembly and shared across all tests
/// using TUnit's <c>[ClassDataSource]</c> attribute with <c>Shared = SharedType.PerAssembly</c>.
/// <para><strong>Schema Initialization:</strong></para>
/// Provides methods to initialize the outbox schema using the provided SQL script.
/// </remarks>
public sealed class PostgreSqlContainerFixture : IAsyncInitializer, IAsyncDisposable
{
    private readonly PostgreSqlContainer _container;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlContainerFixture"/> class.
    /// </summary>
    public PostgreSqlContainerFixture() =>
        _container = new PostgreSqlBuilder("postgres:16-alpine").WithPassword("Test@Password123!").Build();

    /// <summary>
    /// Starts the PostgreSQL container.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await _container.StartAsync().ConfigureAwait(false);
                await WaitUntilPostgreSqlIsReadyAsync().ConfigureAwait(false);
                return;
            }
            catch (Exception) when (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Waits for the PostgreSQL container to become ready for connections with retry logic.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task WaitUntilPostgreSqlIsReadyAsync()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_container.GetConnectionString());
                await connection.OpenAsync().ConfigureAwait(false);
                return;
            }
            catch (Exception) when (attempt < 9)
            {
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Creates a new connection string for a specific database.
    /// </summary>
    /// <param name="databaseName">The database name.</param>
    /// <returns>A connection string for the specified database.</returns>
    public string GetConnectionString(string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = databaseName };
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
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_container.GetConnectionString());
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                // Check if database already exists before creating
                await using var checkCmd = new NpgsqlCommand(
                    $"SELECT 1 FROM pg_database WHERE datname = '{databaseName}'",
                    connection
                );
                var exists = await checkCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

                if (exists is null)
                {
                    await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
                    _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                return;
            }
            catch (Exception) when (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt + 1), cancellationToken).ConfigureAwait(false);
            }
        }
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
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_container.GetConnectionString());
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                // Terminate existing connections
                await using var terminateCmd = new NpgsqlCommand(
                    $"""
                    SELECT pg_terminate_backend(pg_stat_activity.pid)
                    FROM pg_stat_activity
                    WHERE pg_stat_activity.datname = '{databaseName}'
                      AND pid <> pg_backend_pid()
                    """,
                    connection
                );
                _ = await terminateCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\"", connection);
                _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception) when (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt + 1), cancellationToken).ConfigureAwait(false);
            }
        }
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

        // Process psql variable substitutions to make the script compatible with Npgsql
        script = PreprocessPsqlScript(script);

        await using var connection = new NpgsqlConnection(GetConnectionString(databaseName));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(script, connection);
        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
#pragma warning restore CA2100

    /// <summary>
    /// Line separator characters used for splitting SQL script lines.
    /// </summary>
    private static readonly char[] LineSeparators = ['\r', '\n'];

    /// <summary>
    /// Preprocesses a PostgreSQL script to make psql-specific variable syntax compatible with Npgsql.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method handles the conversion of psql-style variable definitions and references
    /// (e.g., <c>\set schema_name 'pulse'</c> and <c>:schema_name</c>) to plain text substitution
    /// that can be executed by Npgsql's SQL command handler.
    /// </para>
    /// <para>
    /// The preprocessing performs the following steps:
    /// <list type="number">
    /// <item>Extracts <c>\set variable_name 'value'</c> declarations.</item>
    /// <item>Removes the <c>\set</c> lines from the script.</item>
    /// <item>Replaces variable references (<c>:"variable_name"</c> and <c>:variable_name</c>) with their values.</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <param name="script">The PostgreSQL script to preprocess.</param>
    /// <returns>The preprocessed script with variable substitutions applied.</returns>
    private static string PreprocessPsqlScript(string script)
    {
        var variables = new Dictionary<string, string>();

        // Extract psql variable definitions (\set variable_name 'value')
        var lines = script.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
        var processedLines = new List<string>();

        foreach (var line in lines)
        {
            // Match \set variable_name 'value' pattern
            if (line.TrimStart().StartsWith(@"\set ", StringComparison.Ordinal))
            {
                var parts = line.TrimStart()[5..].Split(' ', 2);
                if (parts.Length == 2)
                {
                    var varName = parts[0];
                    var varValue = parts[1].Trim('\'');
                    variables[varName] = varValue;
                }
                // Skip the \set line - don't include in output
                continue;
            }

            processedLines.Add(line);
        }

        // Join back the processed lines
        var result = string.Join("\n", processedLines);

        // Replace variable references with their values
        // Order matters: first replace :"variable_name" (quoted), then :variable_name (unquoted)
        foreach (var kvp in variables)
        {
            // Replace :"variable_name" (quoted) - this is the primary psql syntax
            var quotedVar = $":\"{kvp.Key}\"";
            result = result.Replace(quotedVar, kvp.Value, StringComparison.Ordinal);

            // Replace :variable_name (unquoted) as fallback
            var unquotedVar = $":{kvp.Key}";
            result = result.Replace(unquotedVar, kvp.Value, StringComparison.Ordinal);
        }

        return result;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);
}
