namespace NetEvolve.Pulse.Tests.Integration.Internals.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

public sealed class PostgreSqlContainerFixture : IAsyncDisposable, IAsyncInitializer
{
    private const string ScriptDirectory = "/pulse-scripts/";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(
        /*dockerimage*/"postgres:19beta4-trixie"
    )
        .WithLogger(NullLogger.Instance)
        .WithCommand("-c", "max_connections=500") // Raised for parallel integration tests; each test creates its own unique database/pool.
        .WithResourceMapping(
            new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "Scripts", "PostgreSql")),
            ScriptDirectory
        )
        .Build();

    public string ConnectionString => _container.GetConnectionString() + ";Include Error Detail=true;";

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    public async Task InitializeAsync() => await _container.StartAsync().ConfigureAwait(false);

    /// <summary>
    /// Runs a checked-in <c>NetEvolve.Pulse.PostgreSql</c> schema script through the <c>psql</c> client
    /// inside the container, the same way the package README documents it.
    /// </summary>
    /// <exception cref="InvalidOperationException">psql exited with a non-zero exit code.</exception>
    public async Task RunScriptAsync(
        string scriptName,
        string databaseName,
        string schemaName,
        string tableName,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var userName = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()).Username!;

        var result = await _container
            .ExecAsync(
                [
                    "psql",
                    "-X",
                    "-q",
                    "-v",
                    "ON_ERROR_STOP=1",
                    "-v",
                    $"schema_name={schemaName}",
                    "-v",
                    $"table_name={tableName}",
                    "-U",
                    userName,
                    "-d",
                    databaseName,
                    "-f",
                    ScriptDirectory + scriptName,
                ],
                cancellationToken
            )
            .ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"psql -f {scriptName} failed with exit code {result.ExitCode}: {result.Stderr}"
            );
        }
    }
}
