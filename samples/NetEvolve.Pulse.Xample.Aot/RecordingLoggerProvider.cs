namespace NetEvolve.Pulse.Xample.Aot;

using Microsoft.Extensions.Logging;

/// <summary>
/// Logger provider that records every log message, so the smoke run can assert that the built-in logging
/// interceptors ran.
/// </summary>
internal sealed class RecordingLoggerProvider(InvocationRecorder recorder) : ILoggerProvider, ILogger
{
    public ILogger CreateLogger(string categoryName) => this;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    ) => recorder.Invocations.Enqueue(formatter(state, exception));

    public void Dispose() { }
}
