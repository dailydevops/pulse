namespace NetEvolve.Pulse.Tests.Integration;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetEvolve.Pulse.Extensibility;
using TUnit.Core;
using Void = Extensibility.Void;

public sealed class LoggingInterceptorTests
{
    [Test]
    public async Task SendAsync_WithLogging_EmitsBeginAndEndLogEntries()
    {
        using var provider = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        _ = services
            .AddLogging(b => b.AddProvider(provider).SetMinimumLevel(LogLevel.Trace))
            .AddSingleton(TimeProvider.System)
            .AddPulse(config => config.AddLogging())
            .AddScoped<ICommandHandler<LoggingTestCommand, string>, LoggingTestCommandHandler>();

        await using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        _ = await mediator.SendAsync<LoggingTestCommand, string>(new LoggingTestCommand("hello")).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(provider.Entries.Count).IsGreaterThanOrEqualTo(2);
            _ = await Assert
                .That(provider.Entries.Any(e => e.Message.Contains("LoggingTestCommand", StringComparison.Ordinal)))
                .IsTrue();
        }
    }

    [Test]
    public async Task QueryAsync_WithLogging_EmitsBeginAndEndLogEntries()
    {
        using var provider = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        _ = services
            .AddLogging(b => b.AddProvider(provider).SetMinimumLevel(LogLevel.Trace))
            .AddSingleton(TimeProvider.System)
            .AddPulse(config => config.AddLogging())
            .AddScoped<IQueryHandler<LoggingTestQuery, string>, LoggingTestQueryHandler>();

        await using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        _ = await mediator.QueryAsync<LoggingTestQuery, string>(new LoggingTestQuery("world")).ConfigureAwait(false);

        _ = await Assert
            .That(provider.Entries.Any(e => e.Message.Contains("LoggingTestQuery", StringComparison.Ordinal)))
            .IsTrue();
    }

    [Test]
    public async Task PublishAsync_WithLogging_EmitsBeginAndEndLogEntries()
    {
        using var provider = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        _ = services
            .AddLogging(b => b.AddProvider(provider).SetMinimumLevel(LogLevel.Trace))
            .AddSingleton(TimeProvider.System)
            .AddPulse(config => config.AddLogging())
            .AddScoped<IEventHandler<LoggingTestEvent>, LoggingTestEventHandler>();

        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.PublishAsync(new LoggingTestEvent("event-value")).ConfigureAwait(false);
        await Task.Delay(50).ConfigureAwait(false);

        _ = await Assert
            .That(provider.Entries.Any(e => e.Message.Contains("LoggingTestEvent", StringComparison.Ordinal)))
            .IsTrue();
    }

    [Test]
    public async Task SendAsync_WithException_EmitsErrorLogEntry()
    {
        using var provider = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        _ = services
            .AddLogging(b => b.AddProvider(provider).SetMinimumLevel(LogLevel.Trace))
            .AddSingleton(TimeProvider.System)
            .AddPulse(config => config.AddLogging())
            .AddScoped<ICommandHandler<FailingLoggingCommand, Void>, FailingLoggingCommandHandler>();

        await using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await mediator.SendAsync<FailingLoggingCommand, Void>(new FailingLoggingCommand()).ConfigureAwait(false)
        );

        _ = await Assert.That(provider.Entries.Any(e => e.Level == LogLevel.Error)).IsTrue();
    }

    [Test]
    public async Task SendAsync_SlowRequest_EmitsWarningLogEntry()
    {
        using var provider = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        _ = services
            .AddLogging(b => b.AddProvider(provider).SetMinimumLevel(LogLevel.Trace))
            .AddSingleton(TimeProvider.System)
            .AddPulse(config => config.AddLogging(opts => opts.SlowRequestThreshold = TimeSpan.FromMilliseconds(1)))
            .AddScoped<ICommandHandler<SlowLoggingCommand, string>, SlowLoggingCommandHandler>();

        await using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        _ = await mediator.SendAsync<SlowLoggingCommand, string>(new SlowLoggingCommand()).ConfigureAwait(false);

        _ = await Assert.That(provider.Entries.Any(e => e.Level == LogLevel.Warning)).IsTrue();
    }

    // ── Test types ────────────────────────────────────────────────────────────

    private sealed record LoggingTestCommand(string Value) : ICommand<string>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record LoggingTestQuery(string Value) : IQuery<string>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed class LoggingTestEvent : IEvent
    {
        public LoggingTestEvent(string value) => Value = value;

        public string Value { get; }
        public string? CorrelationId { get; set; }
        public string Id { get; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed record FailingLoggingCommand : ICommand<Void>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record SlowLoggingCommand : ICommand<string>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed class LoggingTestCommandHandler : ICommandHandler<LoggingTestCommand, string>
    {
        public Task<string> HandleAsync(LoggingTestCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult(command.Value);
    }

    private sealed class LoggingTestQueryHandler : IQueryHandler<LoggingTestQuery, string>
    {
        public Task<string> HandleAsync(LoggingTestQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(query.Value);
    }

    private sealed class LoggingTestEventHandler : IEventHandler<LoggingTestEvent>
    {
        public Task HandleAsync(LoggingTestEvent message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FailingLoggingCommandHandler : ICommandHandler<FailingLoggingCommand, Void>
    {
        public Task<Void> HandleAsync(FailingLoggingCommand command, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Intentional failure");
    }

    private sealed class SlowLoggingCommandHandler : ICommandHandler<SlowLoggingCommand, string>
    {
        public async Task<string> HandleAsync(SlowLoggingCommand command, CancellationToken cancellationToken = default)
        {
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            return "slow";
        }
    }

    // ── Test logger infrastructure ────────────────────────────────────────────

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<(LogLevel Level, string Message, Exception? Exception)> _entries = [];

        public List<(LogLevel Level, string Message, Exception? Exception)> Entries => _entries;

        public ILogger CreateLogger(string categoryName) => new CapturingLoggerInstance(_entries);

        public void Dispose() { }

        private sealed class CapturingLoggerInstance(List<(LogLevel, string, Exception?)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            ) => entries.Add((logLevel, formatter(state, exception), exception));
        }
    }
}
