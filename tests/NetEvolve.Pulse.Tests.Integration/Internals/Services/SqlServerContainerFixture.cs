namespace NetEvolve.Pulse.Tests.Integration.Internals;

using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.MsSql;
using TUnit.Core.Interfaces;

public sealed class SqlServerContainerFixture : IAsyncDisposable, IAsyncInitializer
{
    // SQL Server 2025 containers crash intermittently during startup (sqlpal/LSASS fatal error, Reason 0x00000002),
    // more often when several start concurrently, as they do here (one per target framework). This happens on the
    // ubuntu-24.04 and the ubuntu-22.04 base, see https://github.com/microsoft/mssql-docker/issues/974. A crashed
    // container is replaced by a fresh one (bounded), and every retry is reported.
    private const int MaxStartAttempts = 3;

    private MsSqlContainer? _container;

    public string ConnectionString =>
        (
            _container ?? throw new InvalidOperationException("The SQL Server container has not been started.")
        ).GetConnectionString() + ";MultipleActiveResultSets=True;";

    public ValueTask DisposeAsync() => _container?.DisposeAsync() ?? ValueTask.CompletedTask;

    public async Task InitializeAsync()
    {
        for (var attempt = 1; attempt < MaxStartAttempts; attempt++)
        {
            var container = CreateContainer();
            try
            {
                await container.StartAsync().ConfigureAwait(false);
                _container = container;
                return;
            }
            catch (ContainerNotRunningException ex)
            {
                await ReportCrashAsync(attempt, ex).ConfigureAwait(false);
                await container.DisposeAsync().ConfigureAwait(false);
            }
        }

        // Last attempt: let a further crash fail the tests with the container output.
        _container = CreateContainer();
        await _container.StartAsync().ConfigureAwait(false);
    }

    // Test output of passing runs is not printed by `dotnet test`, so a retry is also written to the GitHub Actions job
    // summary (when running there) to keep the upstream crash rate observable.
    private static async Task ReportCrashAsync(int attempt, ContainerNotRunningException ex)
    {
        var message =
            $"[SqlServerContainerFixture] SQL Server container crashed during startup (attempt {attempt}/{MaxStartAttempts}, {TestHelper.TargetFramework}), starting a new one.";
        await Console.Error.WriteLineAsync($"{message}{Environment.NewLine}{ex.Message}").ConfigureAwait(false);

        var summary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        if (!string.IsNullOrEmpty(summary))
        {
            await File.AppendAllTextAsync(
                    summary,
                    $"> [!WARNING]{Environment.NewLine}> {message}{Environment.NewLine}{Environment.NewLine}"
                )
                .ConfigureAwait(false);
        }
    }

    private static MsSqlContainer CreateContainer()
    {
        var builder = new MsSqlBuilder(
            /*dockerimage*/"mcr.microsoft.com/mssql/server:2025-CU9-ubuntu-22.04"
        );
        return builder.WithLogger(NullLogger.Instance).Build();
    }
}
