namespace NetEvolve.Pulse.Tests.Integration.Pipeline;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Caching;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

/// <summary>
/// Integration tests exercising built-in Pulse interceptors (distributed cache query caching,
/// request timeout enforcement, and structured logging) end-to-end through a real, fully built
/// <see cref="IMediator"/> pipeline. Unlike the outbox/idempotency tests in this project, these
/// tests do not require any real database or broker container.
/// </summary>
[TestGroup("Pipeline")]
public sealed class CachingTimeoutLoggingTests
{
    private static async Task<IHost> BuildHostAsync(
        Action<IServiceCollection> configureServices,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var host = new HostBuilder().ConfigureServices(configureServices).Build();

        await host.StartAsync(cancellationToken).ConfigureAwait(false);

        return host;
    }

    // ---------------------------------------------------------------------
    // DistributedCacheQueryInterceptor
    // ---------------------------------------------------------------------

    [Test]
    public async Task DistributedCacheQueryInterceptor_Should_Only_Invoke_Handler_Once_For_Same_CacheKey(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var executionCount = 0;

        using var host = await BuildHostAsync(
                services =>
                {
                    _ = services.AddDistributedMemoryCache();
                    _ = services.AddSingleton<IQueryHandler<CachingTestQuery, int>>(
                        new CachingTestQueryHandler(() => Interlocked.Increment(ref executionCount))
                    );
                    _ = services.AddPulse(builder => builder.AddQueryCaching());
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var query = new CachingTestQuery("cache-key-1");

            var first = await mediator
                .QueryAsync<CachingTestQuery, int>(query, cancellationToken)
                .ConfigureAwait(false);
            var second = await mediator
                .QueryAsync<CachingTestQuery, int>(query, cancellationToken)
                .ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(first).IsEqualTo(second);
                _ = await Assert.That(executionCount).IsEqualTo(1);
            }
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    [Test]
    public async Task DistributedCacheQueryInterceptor_Should_Invoke_Handler_For_Each_DistinctCacheKey(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var executionCount = 0;

        using var host = await BuildHostAsync(
                services =>
                {
                    _ = services.AddDistributedMemoryCache();
                    _ = services.AddSingleton<IQueryHandler<CachingTestQuery, int>>(
                        new CachingTestQueryHandler(() => Interlocked.Increment(ref executionCount))
                    );
                    _ = services.AddPulse(builder => builder.AddQueryCaching());
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            _ = await mediator
                .QueryAsync<CachingTestQuery, int>(new CachingTestQuery("cache-key-a"), cancellationToken)
                .ConfigureAwait(false);
            _ = await mediator
                .QueryAsync<CachingTestQuery, int>(new CachingTestQuery("cache-key-b"), cancellationToken)
                .ConfigureAwait(false);

            _ = await Assert.That(executionCount).IsEqualTo(2);
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------------
    // TimeoutRequestInterceptor / TimeoutStreamQueryInterceptor
    // ---------------------------------------------------------------------

    [Test]
    public async Task TimeoutRequestInterceptor_Should_Throw_TimeoutException_When_HandlerExceedsDeadline(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = await BuildHostAsync(
                services =>
                {
                    _ = services.AddSingleton<ICommandHandler<SlowTimeoutCommand, string>, SlowTimeoutCommandHandler>();
                    _ = services.AddPulse(builder => builder.AddRequestTimeout());
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var command = new SlowTimeoutCommand(TimeSpan.FromMilliseconds(50));

            _ = await Assert.ThrowsAsync<TimeoutException>(async () =>
                await mediator.SendAsync<SlowTimeoutCommand, string>(command, cancellationToken).ConfigureAwait(false)
            );
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    [Test]
    public async Task TimeoutRequestInterceptor_Should_Complete_Normally_When_HandlerCompletesWithinDeadline(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = await BuildHostAsync(
                services =>
                {
                    _ = services.AddSingleton<ICommandHandler<SlowTimeoutCommand, string>, SlowTimeoutCommandHandler>();
                    _ = services.AddPulse(builder => builder.AddRequestTimeout());
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var command = new SlowTimeoutCommand(TimeSpan.FromSeconds(5));

            var result = await mediator
                .SendAsync<SlowTimeoutCommand, string>(command, cancellationToken)
                .ConfigureAwait(false);

            _ = await Assert.That(result).IsEqualTo("completed");
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    [Test]
    public async Task TimeoutStreamQueryInterceptor_Should_Throw_TimeoutException_When_HandlerExceedsDeadline(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = await BuildHostAsync(
                services =>
                {
                    _ = services.AddSingleton<
                        IStreamQueryHandler<SlowTimeoutStreamQuery, int>,
                        SlowTimeoutStreamQueryHandler
                    >();
                    _ = services.AddPulse(builder => builder.AddRequestTimeout());
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var query = new SlowTimeoutStreamQuery(TimeSpan.FromMilliseconds(50));

            _ = await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await foreach (
                    var item in mediator.StreamQueryAsync<SlowTimeoutStreamQuery, int>(query, cancellationToken)
                )
                {
                    _ = item;
                }
            });
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------------
    // Logging interceptors
    // ---------------------------------------------------------------------

    [Test]
    public async Task LoggingEventInterceptor_Should_Record_Begin_And_End_LogEntries(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var provider = new CapturingLoggerProvider();

        using var host = await BuildHostAsync(
                services =>
                {
                    _ = services.AddLogging(logging =>
                    {
                        _ = logging.ClearProviders();
                        _ = logging.AddProvider(provider);
                        _ = logging.SetMinimumLevel(LogLevel.Trace);
                    });
                    _ = services.AddSingleton<IEventHandler<FastLoggingEvent>, FastLoggingEventHandler>();
                    _ = services.AddPulse(builder => builder.AddLogging());
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            await mediator.PublishAsync(new FastLoggingEvent(), cancellationToken).ConfigureAwait(false);
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);

        var records = provider.Records.ToArray();

        using (Assert.Multiple())
        {
            _ = await Assert
                .That(records.Any(r => r.Message.Contains("FastLoggingEvent", StringComparison.Ordinal)))
                .IsTrue();
            _ = await Assert
                .That(records.Any(r => r.Message.StartsWith("Handling", StringComparison.Ordinal)))
                .IsTrue();
            _ = await Assert
                .That(records.Any(r => r.Message.StartsWith("Handled ", StringComparison.Ordinal)))
                .IsTrue();
        }
    }

    [Test]
    public async Task LoggingStreamQueryInterceptor_Should_Record_Begin_And_End_LogEntries(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var provider = new CapturingLoggerProvider();

        using var host = await BuildHostAsync(
                services =>
                {
                    _ = services.AddLogging(logging =>
                    {
                        _ = logging.ClearProviders();
                        _ = logging.AddProvider(provider);
                        _ = logging.SetMinimumLevel(LogLevel.Trace);
                    });
                    _ = services.AddSingleton<
                        IStreamQueryHandler<FastLoggingStreamQuery, int>,
                        FastLoggingStreamQueryHandler
                    >();
                    _ = services.AddPulse(builder => builder.AddLogging());
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var items = new List<int>();

            await foreach (
                var item in mediator.StreamQueryAsync<FastLoggingStreamQuery, int>(
                    new FastLoggingStreamQuery(),
                    cancellationToken
                )
            )
            {
                items.Add(item);
            }

            _ = await Assert.That(items).IsEquivalentTo([1, 2]);
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);

        var records = provider.Records.ToArray();

        using (Assert.Multiple())
        {
            _ = await Assert
                .That(records.Any(r => r.Message.Contains("FastLoggingStreamQuery", StringComparison.Ordinal)))
                .IsTrue();
            // Stream queries log "Streaming ..." (begin) / "Streamed ... in Xms" (end) — see
            // LoggingMessages.LogBeginStreamQuery / LogEndStreamQuery.
            _ = await Assert
                .That(records.Any(r => r.Message.StartsWith("Streaming", StringComparison.Ordinal)))
                .IsTrue();
            _ = await Assert
                .That(records.Any(r => r.Message.StartsWith("Streamed ", StringComparison.Ordinal)))
                .IsTrue();
        }
    }

    [Test]
    public async Task LoggingRequestInterceptor_Should_Record_Begin_And_End_LogEntries(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var provider = new CapturingLoggerProvider();

        using var host = await BuildHostAsync(
                services =>
                {
                    _ = services.AddLogging(logging =>
                    {
                        _ = logging.ClearProviders();
                        _ = logging.AddProvider(provider);
                        _ = logging.SetMinimumLevel(LogLevel.Trace);
                    });
                    _ = services.AddSingleton<ICommandHandler<FastLoggingCommand, string>, FastLoggingCommandHandler>();
                    _ = services.AddPulse(builder => builder.AddLogging());
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var result = await mediator
                .SendAsync<FastLoggingCommand, string>(new FastLoggingCommand(), cancellationToken)
                .ConfigureAwait(false);

            _ = await Assert.That(result).IsEqualTo("fast");
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);

        var records = provider.Records.ToArray();

        using (Assert.Multiple())
        {
            _ = await Assert
                .That(records.Any(r => r.Message.Contains("FastLoggingCommand", StringComparison.Ordinal)))
                .IsTrue();
            // "Handling ..." is emitted before the handler runs (begin), "Handled ... in Xms" after it
            // completes successfully (end) — see LoggingMessages.LogBeginHandle / LogEndHandle.
            _ = await Assert
                .That(records.Any(r => r.Message.StartsWith("Handling", StringComparison.Ordinal)))
                .IsTrue();
            _ = await Assert
                .That(records.Any(r => r.Message.StartsWith("Handled ", StringComparison.Ordinal)))
                .IsTrue();
        }
    }

    [Test]
    public async Task LoggingRequestInterceptor_Should_Record_SlowRequest_Warning_When_ThresholdExceeded(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var provider = new CapturingLoggerProvider();

        using var host = await BuildHostAsync(
                services =>
                {
                    _ = services.AddLogging(logging =>
                    {
                        _ = logging.ClearProviders();
                        _ = logging.AddProvider(provider);
                        _ = logging.SetMinimumLevel(LogLevel.Trace);
                    });
                    _ = services.AddSingleton<ICommandHandler<SlowLoggingCommand, string>, SlowLoggingCommandHandler>();
                    _ = services.AddPulse(builder =>
                        builder.AddLogging(opts => opts.SlowRequestThreshold = TimeSpan.FromMilliseconds(10))
                    );
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            _ = await mediator
                .SendAsync<SlowLoggingCommand, string>(new SlowLoggingCommand(), cancellationToken)
                .ConfigureAwait(false);
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);

        var records = provider.Records.ToArray();

        _ = await Assert.That(records.Any(r => r.Level == LogLevel.Warning)).IsTrue();
    }

    // ---------------------------------------------------------------------
    // NullMessageTransport
    // ---------------------------------------------------------------------

    [Test]
    public async Task NullMessageTransport_SendAsync_Should_CompleteWithoutError(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var transport = new NullMessageTransport();
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = typeof(object),
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = OutboxMessageStatus.Processing,
        };

        await transport.SendAsync(message, cancellationToken).ConfigureAwait(false);
    }

    [Test]
    public async Task IMessageTransport_DefaultSendBatchAsync_Sends_AllMessages_Sequentially_InOrder(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        IMessageTransport transport = new RecordingMessageTransport();
        var messages = Enumerable
            .Range(0, 3)
            .Select(i => new OutboxMessage
            {
                Id = Guid.NewGuid(),
                EventType = typeof(object),
                Payload = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Status = OutboxMessageStatus.Processing,
            })
            .ToList();

        // Exercised through the interface reference so the default SendBatchAsync/SendBatchInternalAsync
        // implementation on IMessageTransport runs (RecordingMessageTransport only overrides SendAsync).
        await transport.SendBatchAsync(messages, cancellationToken).ConfigureAwait(false);

        var recorder = (RecordingMessageTransport)transport;
        _ = await Assert.That(recorder.SentPayloads).IsEquivalentTo(["0", "1", "2"]);
    }

    [Test]
    public async Task IMessageTransport_DefaultSendBatchAsync_WithNullMessages_ThrowsArgumentNullException()
    {
        IMessageTransport transport = new RecordingMessageTransport();

        _ = await Assert.That(() => transport.SendBatchAsync(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task IMessageTransport_DefaultIsHealthyAsync_ReturnsTrue(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IMessageTransport transport = new RecordingMessageTransport();

        var healthy = await transport.IsHealthyAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(healthy).IsTrue();
    }

    // ---------------------------------------------------------------------
    // Test doubles - Caching
    // ---------------------------------------------------------------------

    /// <summary>
    /// Minimal <see cref="IMessageTransport"/> implementation that only overrides <see cref="SendAsync"/>,
    /// so calling <see cref="IMessageTransport.SendBatchAsync"/>/<see cref="IMessageTransport.IsHealthyAsync"/>
    /// through the interface exercises the interface's own default implementations.
    /// </summary>
    private sealed class RecordingMessageTransport : IMessageTransport
    {
        private readonly ConcurrentQueue<string> _sentPayloads = new();

        public IReadOnlyList<string> SentPayloads => [.. _sentPayloads];

        public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _sentPayloads.Enqueue(message.Payload);
            return Task.CompletedTask;
        }
    }

    private sealed record CachingTestQuery(string Id) : ICacheableQuery<int>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }

        public string CacheKey => $"pipeline-caching-test:{Id}";

        public TimeSpan? Expiry => TimeSpan.FromMinutes(5);
    }

    private sealed class CachingTestQueryHandler(Action onExecuted) : IQueryHandler<CachingTestQuery, int>
    {
        private int _value;

        public Task<int> HandleAsync(CachingTestQuery request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            onExecuted();
            return Task.FromResult(Interlocked.Increment(ref _value));
        }
    }

    // ---------------------------------------------------------------------
    // Test doubles - Timeout
    // ---------------------------------------------------------------------

    private sealed record SlowTimeoutCommand(TimeSpan Timeout) : ICommand<string>, ITimeoutRequest
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }

        TimeSpan? ITimeoutRequest.Timeout => Timeout;
    }

    private sealed class SlowTimeoutCommandHandler : ICommandHandler<SlowTimeoutCommand, string>
    {
        public async Task<string> HandleAsync(SlowTimeoutCommand command, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Always delay longer than the "fast" test's timeout, but shorter than the "slow" test's timeout,
            // so both the timeout and the pass-through scenario can be exercised deterministically.
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            return "completed";
        }
    }

    private sealed record SlowTimeoutStreamQuery(TimeSpan Timeout) : IStreamQuery<int>, ITimeoutRequest
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }

        TimeSpan? ITimeoutRequest.Timeout => Timeout;
    }

    private sealed class SlowTimeoutStreamQueryHandler : IStreamQueryHandler<SlowTimeoutStreamQuery, int>
    {
        public async IAsyncEnumerable<int> HandleAsync(
            SlowTimeoutStreamQuery request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            yield return 1;
        }
    }

    // ---------------------------------------------------------------------
    // Test doubles - Logging
    // ---------------------------------------------------------------------

    private sealed record FastLoggingCommand : ICommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class FastLoggingCommandHandler : ICommandHandler<FastLoggingCommand, string>
    {
        public Task<string> HandleAsync(FastLoggingCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult("fast");
    }

    private sealed record SlowLoggingCommand : ICommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class SlowLoggingCommandHandler : ICommandHandler<SlowLoggingCommand, string>
    {
        public async Task<string> HandleAsync(SlowLoggingCommand command, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
            return "slow";
        }
    }

    private sealed record FastLoggingEvent : IEvent
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class FastLoggingEventHandler : IEventHandler<FastLoggingEvent>
    {
        public Task HandleAsync(FastLoggingEvent @event, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed record FastLoggingStreamQuery : IStreamQuery<int>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class FastLoggingStreamQueryHandler : IStreamQueryHandler<FastLoggingStreamQuery, int>
    {
        public async IAsyncEnumerable<int> HandleAsync(
            FastLoggingStreamQuery request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return 1;
            await Task.Yield();
            yield return 2;
        }
    }

    /// <summary>
    /// Minimal in-memory <see cref="ILoggerProvider"/> that captures every log entry emitted through it,
    /// so tests can assert on structured log output produced by the built-in logging interceptors.
    /// </summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentBag<CapturedLogRecord> Records { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Records);

        public void Dispose() { }

        private sealed class CapturingLogger(string categoryName, ConcurrentBag<CapturedLogRecord> records) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                ArgumentNullException.ThrowIfNull(formatter);
                records.Add(new CapturedLogRecord(categoryName, logLevel, eventId, formatter(state, exception)));
            }
        }
    }

    private sealed record CapturedLogRecord(string Category, LogLevel Level, EventId EventId, string Message);
}
