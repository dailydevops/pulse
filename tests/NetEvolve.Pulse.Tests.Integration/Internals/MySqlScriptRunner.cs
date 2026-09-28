namespace NetEvolve.Pulse.Tests.Integration.Internals;

using MySql.Data.MySqlClient;

/// <summary>
/// Executes the checked-in <c>NetEvolve.Pulse.MySql</c> schema scripts statement by statement,
/// the same way the plain <c>mysql</c> client does with its default <c>;</c> delimiter.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "SQL is read from the checked-in provider script with a test-controlled table name substituted."
)]
internal static class MySqlScriptRunner
{
    /// <summary>
    /// Runs the given script from <c>Scripts/MySql</c> against <paramref name="connectionString"/>,
    /// replacing every table-name reference of <paramref name="defaultTableName"/> with <paramref name="tableName"/>.
    /// </summary>
    /// <remarks>
    /// Only table references are replaced (<c>CREATE TABLE</c>, <c>ON `X`</c> and <c>TABLE_NAME = 'X'</c>),
    /// so a column that shares the table name (for example <c>IdempotencyKey</c>) keeps its name.
    /// </remarks>
    public static async Task ExecuteAsync(
        string connectionString,
        string scriptName,
        string defaultTableName,
        string tableName,
        CancellationToken cancellationToken
    )
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Scripts", "MySql", scriptName);
        var script = await File.ReadAllTextAsync(scriptPath, cancellationToken).ConfigureAwait(false);

        script = script
            .Replace(
                $"TABLE IF NOT EXISTS `{defaultTableName}`",
                $"TABLE IF NOT EXISTS `{tableName}`",
                StringComparison.Ordinal
            )
            .Replace($" ON `{defaultTableName}`", $" ON `{tableName}`", StringComparison.Ordinal)
            .Replace($"TABLE_NAME = '{defaultTableName}'", $"TABLE_NAME = '{tableName}'", StringComparison.Ordinal);

        // The scripts keep their guard state in session user variables (@pulse_sql),
        // which MySql.Data only passes through when AllowUserVariables is enabled.
        var builder = new MySqlConnectionStringBuilder(connectionString) { AllowUserVariables = true };

        var connection = new MySqlConnection(builder.ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            foreach (
                var statement in script.Split(
                    ';',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
            )
            {
                if (IsCommentOrEmpty(statement))
                {
                    continue;
                }

                var command = new MySqlCommand(statement, connection);
                await using (command.ConfigureAwait(false))
                {
                    _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private static bool IsCommentOrEmpty(string statement)
    {
        foreach (var line in statement.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && !trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
