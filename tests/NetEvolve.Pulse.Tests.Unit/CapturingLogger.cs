namespace NetEvolve.Pulse.Tests.Unit;

using Microsoft.Extensions.Logging;

/// <summary>
/// A minimal <see cref="ILogger{T}"/> implementation that captures log entries for assertions in unit tests.
/// </summary>
/// <typeparam name="T">The category type.</typeparam>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<LogEntry> _entries = [];

    /// <summary>Gets the captured log entries.</summary>
    public IReadOnlyList<LogEntry> Entries => _entries;

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    ) => _entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
}

/// <summary>Represents a captured log entry.</summary>
/// <param name="Level">The log level.</param>
/// <param name="Message">The formatted message.</param>
/// <param name="Exception">The exception, if any.</param>
internal sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
