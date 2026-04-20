namespace NetEvolve.Pulse.Tests.Integration.Internals;

internal sealed class SQLiteDatabaseServiceFixture : IServiceType
{
    public string ConnectionString => $"Data Source={DatabaseFile};";

    public ServiceType ServiceType => ServiceType.SQLite;

    private string DatabaseFile { get; } =
        Path.Combine(Path.GetTempPath(), $"{TestHelper.TargetFramework}{Guid.NewGuid():N}.sqlite");

    public ValueTask DisposeAsync()
    {
        if (!File.Exists(DatabaseFile))
        {
            return ValueTask.CompletedTask;
        }

        try
        {
            File.Delete(DatabaseFile);
        }
        catch (IOException)
        {
            // Best-effort cleanup for temporary test database files.
        }

        return ValueTask.CompletedTask;
    }

    public Task InitializeAsync() => Task.CompletedTask;
}
