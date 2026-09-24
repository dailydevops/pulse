namespace NetEvolve.Pulse.Tests.Integration.Pipeline;

using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// Integration tests that exercise the built-in <c>ActivityAndMetrics</c>, <c>ConcurrentCommandGuard</c>,
/// and <c>DataAnnotations</c> interceptors end-to-end through a real, fully built <see cref="IMediator"/>
/// pipeline (no mocking of the pipeline itself). Each test builds a minimal, self-contained host via
/// <c>AddPulse(...)</c> — no database, broker, or Testcontainers dependency required.
/// </summary>
[TestGroup("Pipeline")]
[TestGroup("Interceptors")]
public sealed class RequestInterceptorsTests
{
    private static async Task<IHost> CreateHostAsync(
        Action<IMediatorBuilder> configureMediator,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var host = new HostBuilder()
            .ConfigureServices(services => services.AddPulse(configureMediator))
            .ConfigureWebHost(webBuilder => _ = webBuilder.UseTestServer().Configure(applicationBuilder => { }))
            .Build();

        await host.StartAsync(cancellationToken).ConfigureAwait(false);

        return host;
    }

    [Test]
    [NotInParallel]
    public async Task ActivityAndMetrics_Command_RecordsActivityAndMetrics(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "NetEvolve.Pulse", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(activityListener);

        Activity? capturedActivity = null;
        activityListener.ActivityStopped = activity =>
        {
            if (string.Equals(activity.DisplayName, "Command.ActivityMetricsCommand", StringComparison.Ordinal))
            {
                capturedActivity = activity;
            }
        };

        long counterValue = 0;
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (
                    string.Equals(instrument.Meter.Name, "NetEvolve.Pulse", StringComparison.Ordinal)
                    && string.Equals(instrument.Name, "pulse.requests.total", StringComparison.Ordinal)
                )
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        meterListener.SetMeasurementEventCallback<long>(
            (_, measurement, _, _) => Interlocked.Add(ref counterValue, measurement)
        );
        meterListener.Start();

        using var host = await CreateHostAsync(
                mediatorBuilder =>
                    mediatorBuilder
                        .AddActivityAndMetrics()
                        .AddCommandHandler<ActivityMetricsCommand, string, ActivityMetricsCommandHandler>(),
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        string result;
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            result = await mediator
                .SendAsync<ActivityMetricsCommand, string>(new ActivityMetricsCommand(), cancellationToken)
                .ConfigureAwait(false);
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo("handled");
            _ = await Assert.That(capturedActivity).IsNotNull();
            _ = await Assert.That(capturedActivity!.Status).IsEqualTo(ActivityStatusCode.Ok);
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.request.type")).IsEqualTo("Command");
            _ = await Assert.That(Interlocked.Read(ref counterValue)).IsGreaterThanOrEqualTo(1L);
        }
    }

    [Test]
    [NotInParallel]
    public async Task ActivityAndMetrics_Query_RecordsActivityAndMetrics(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "NetEvolve.Pulse", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(activityListener);

        Activity? capturedActivity = null;
        activityListener.ActivityStopped = activity =>
        {
            if (string.Equals(activity.DisplayName, "Query.ActivityMetricsQuery", StringComparison.Ordinal))
            {
                capturedActivity = activity;
            }
        };

        using var host = await CreateHostAsync(
                mediatorBuilder =>
                    mediatorBuilder
                        .AddActivityAndMetrics()
                        .AddQueryHandler<ActivityMetricsQuery, int, ActivityMetricsQueryHandler>(),
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        int result;
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            result = await mediator
                .QueryAsync<ActivityMetricsQuery, int>(new ActivityMetricsQuery(), cancellationToken)
                .ConfigureAwait(false);
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result).IsEqualTo(42);
            _ = await Assert.That(capturedActivity).IsNotNull();
            _ = await Assert.That(capturedActivity!.Status).IsEqualTo(ActivityStatusCode.Ok);
            _ = await Assert.That(capturedActivity.GetTagItem("pulse.request.type")).IsEqualTo("Query");
        }
    }

    [Test]
    [NotInParallel]
    public async Task ActivityAndMetrics_Event_RecordsActivityAndMetrics(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "NetEvolve.Pulse", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(activityListener);

        Activity? capturedActivity = null;
        activityListener.ActivityStopped = activity =>
        {
            if (string.Equals(activity.DisplayName, "Event.ActivityMetricsEvent", StringComparison.Ordinal))
            {
                capturedActivity = activity;
            }
        };

        long counterValue = 0;
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (
                    string.Equals(instrument.Meter.Name, "NetEvolve.Pulse", StringComparison.Ordinal)
                    && string.Equals(instrument.Name, "pulse.events.total", StringComparison.Ordinal)
                )
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        meterListener.SetMeasurementEventCallback<long>(
            (_, measurement, _, _) => Interlocked.Add(ref counterValue, measurement)
        );
        meterListener.Start();

        using var host = await CreateHostAsync(
                mediatorBuilder =>
                    mediatorBuilder
                        .AddActivityAndMetrics()
                        .AddEventHandler<ActivityMetricsEvent, ActivityMetricsEventHandler>(),
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            await mediator
                .PublishAsync(new ActivityMetricsEvent { Id = "evt-001" }, cancellationToken)
                .ConfigureAwait(false);
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(capturedActivity).IsNotNull();
            _ = await Assert.That(capturedActivity!.Status).IsEqualTo(ActivityStatusCode.Ok);
            _ = await Assert.That(Interlocked.Read(ref counterValue)).IsGreaterThanOrEqualTo(1L);
        }
    }

    [Test]
    [NotInParallel]
    public async Task ActivityAndMetrics_StreamQuery_RecordsActivityAndMetrics(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "NetEvolve.Pulse", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(activityListener);

        Activity? capturedActivity = null;
        activityListener.ActivityStopped = activity =>
        {
            if (string.Equals(activity.DisplayName, "StreamQuery.ActivityMetricsStreamQuery", StringComparison.Ordinal))
            {
                capturedActivity = activity;
            }
        };

        using var host = await CreateHostAsync(
                mediatorBuilder =>
                    mediatorBuilder
                        .AddActivityAndMetrics()
                        .AddStreamQueryHandler<ActivityMetricsStreamQuery, int, ActivityMetricsStreamQueryHandler>(),
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        List<int> items;
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            items = new List<int>();
            await foreach (
                var item in mediator
                    .StreamQueryAsync<ActivityMetricsStreamQuery, int>(
                        new ActivityMetricsStreamQuery(),
                        cancellationToken
                    )
                    .WithCancellation(cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                items.Add(item);
            }
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(items).IsEquivalentTo([1, 2, 3]);
            _ = await Assert.That(capturedActivity).IsNotNull();
            _ = await Assert.That(capturedActivity!.Status).IsEqualTo(ActivityStatusCode.Ok);
        }
    }

    [Test]
    [NotInParallel]
    public async Task ConcurrentCommandGuard_ConcurrentSends_SerializesExecution(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = await CreateHostAsync(
                mediatorBuilder =>
                    mediatorBuilder
                        .AddConcurrentCommandGuard()
                        .AddCommandHandler<GuardedCommand, int, GuardedCommandHandler>(),
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var tasks = Enumerable
                .Range(0, 5)
                .Select(_ => mediator.SendAsync<GuardedCommand, int>(new GuardedCommand(), cancellationToken))
                .ToArray();

            _ = await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(GuardedCommandHandler.MaxConcurrent).IsEqualTo(1);
    }

    [Test]
    [NotInParallel]
    public async Task ConcurrentCommandGuard_TypedOverload_ConcurrentSends_SerializesExecution(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = await CreateHostAsync(
                mediatorBuilder =>
                    mediatorBuilder
                        .AddConcurrentCommandGuard<GuardedCommand, int>()
                        .AddCommandHandler<GuardedCommand, int, GuardedCommandHandler>(),
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var tasks = Enumerable
                .Range(0, 5)
                .Select(_ => mediator.SendAsync<GuardedCommand, int>(new GuardedCommand(), cancellationToken))
                .ToArray();

            _ = await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(GuardedCommandHandler.MaxConcurrent).IsEqualTo(1);
    }

    [Test]
    public async Task ConcurrentCommandGuard_VoidOverload_ConcurrentSends_SerializesExecution(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = await CreateHostAsync(
                mediatorBuilder =>
                    mediatorBuilder
                        .AddConcurrentCommandGuard<GuardedVoidCommand>()
                        .AddCommandHandler<GuardedVoidCommand, GuardedVoidCommandHandler>(),
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var tasks = Enumerable
                .Range(0, 5)
                .Select(_ => mediator.SendAsync<GuardedVoidCommand, Void>(new GuardedVoidCommand(), cancellationToken))
                .ToArray();

            _ = await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(GuardedVoidCommandHandler.MaxConcurrent).IsEqualTo(1);
    }

    [Test]
    public async Task DataAnnotations_InvalidCommand_ThrowsValidationException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = await CreateHostAsync(
                mediatorBuilder =>
                    mediatorBuilder
                        .AddDataAnnotations()
                        .AddCommandHandler<ValidatedCommand, string, ValidatedCommandHandler>(),
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

#pragma warning disable S8969 // required to match the Func<..., Task<string?>> overload TUnit infers here
            _ = await Assert
                .That(() =>
                    mediator.SendAsync<ValidatedCommand, string>(
                        new ValidatedCommand { Name = null! },
                        cancellationToken
                    )!
                )
                .Throws<ValidationException>();
#pragma warning restore S8969
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    [Test]
    public async Task DataAnnotations_ValidCommand_Succeeds(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = await CreateHostAsync(
                mediatorBuilder =>
                    mediatorBuilder
                        .AddDataAnnotations()
                        .AddCommandHandler<ValidatedCommand, string, ValidatedCommandHandler>(),
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        string result;
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            result = await mediator
                .SendAsync<ValidatedCommand, string>(new ValidatedCommand { Name = "World" }, cancellationToken)
                .ConfigureAwait(false);
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(result).IsEqualTo("hello World");
    }

    [Test]
    public async Task DataAnnotations_InvalidEvent_ThrowsValidationException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = await CreateHostAsync(
                mediatorBuilder =>
                    mediatorBuilder.AddDataAnnotations().AddEventHandler<ValidatedEvent, ValidatedEventHandler>(),
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            _ = await Assert
                .That(() =>
                    mediator.PublishAsync(new ValidatedEvent { Id = "evt-001", Name = null! }, cancellationToken)
                )
                .Throws<ValidationException>();

            _ = await Assert.That(ValidatedEventHandler.HandlerInvoked).IsFalse();
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    [Test]
    public async Task DataAnnotations_InvalidStreamQuery_ThrowsValidationException(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = await CreateHostAsync(
                mediatorBuilder =>
                    mediatorBuilder
                        .AddDataAnnotations()
                        .AddStreamQueryHandler<ValidatedStreamQuery, string, ValidatedStreamQueryHandler>(),
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            _ = await Assert
                .That(async () =>
                {
                    await foreach (
                        var item in mediator.StreamQueryAsync<ValidatedStreamQuery, string>(
                            new ValidatedStreamQuery { Name = null! },
                            cancellationToken
                        )
                    )
                    {
                        _ = item;
                    }
                })
                .Throws<ValidationException>();
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    [Test]
    public async Task DataAnnotations_ValidStreamQuery_YieldsItems(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var host = await CreateHostAsync(
                mediatorBuilder =>
                    mediatorBuilder
                        .AddDataAnnotations()
                        .AddStreamQueryHandler<ValidatedStreamQuery, string, ValidatedStreamQueryHandler>(),
                cancellationToken
            )
            .ConfigureAwait(false);

        var scope = host.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var items = new List<string>();

            await foreach (
                var item in mediator.StreamQueryAsync<ValidatedStreamQuery, string>(
                    new ValidatedStreamQuery { Name = "World" },
                    cancellationToken
                )
            )
            {
                items.Add(item);
            }

            _ = await Assert.That(items).IsEquivalentTo(["hello World"]);
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record ActivityMetricsCommand : ICommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class ActivityMetricsCommandHandler : ICommandHandler<ActivityMetricsCommand, string>
    {
        public Task<string> HandleAsync(
            ActivityMetricsCommand command,
            CancellationToken cancellationToken = default
        ) => Task.FromResult("handled");
    }

    private sealed record ActivityMetricsQuery : IQuery<int>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class ActivityMetricsQueryHandler : IQueryHandler<ActivityMetricsQuery, int>
    {
        public Task<int> HandleAsync(ActivityMetricsQuery request, CancellationToken cancellationToken = default) =>
            Task.FromResult(42);
    }

    private sealed record ActivityMetricsEvent : IEvent
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }

        public required string Id { get; init; }

        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class ActivityMetricsEventHandler : IEventHandler<ActivityMetricsEvent>
    {
        public Task HandleAsync(ActivityMetricsEvent message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed record ActivityMetricsStreamQuery : IStreamQuery<int>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class ActivityMetricsStreamQueryHandler : IStreamQueryHandler<ActivityMetricsStreamQuery, int>
    {
        public async IAsyncEnumerable<int> HandleAsync(
            ActivityMetricsStreamQuery request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var value in new[] { 1, 2, 3 })
            {
                await Task.Yield();
                yield return value;
            }
        }
    }

    private sealed record GuardedCommand : IExclusiveCommand<int>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class GuardedCommandHandler : ICommandHandler<GuardedCommand, int>
    {
        private static int _currentConcurrent;
        private static int _maxConcurrent;

        public static int MaxConcurrent => _maxConcurrent;

        public async Task<int> HandleAsync(GuardedCommand command, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = Interlocked.Increment(ref _currentConcurrent);
            var max = _maxConcurrent;
            while (current > max)
            {
                _ = Interlocked.CompareExchange(ref _maxConcurrent, current, max);
                max = _maxConcurrent;
            }

            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            _ = Interlocked.Decrement(ref _currentConcurrent);

            return current;
        }
    }

    private sealed record GuardedVoidCommand : IExclusiveCommand
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class GuardedVoidCommandHandler : ICommandHandler<GuardedVoidCommand, Void>
    {
        private static int _currentConcurrent;
        private static int _maxConcurrent;

        public static int MaxConcurrent => _maxConcurrent;

        public async Task<Void> HandleAsync(GuardedVoidCommand command, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = Interlocked.Increment(ref _currentConcurrent);
            var max = _maxConcurrent;
            while (current > max)
            {
                _ = Interlocked.CompareExchange(ref _maxConcurrent, current, max);
                max = _maxConcurrent;
            }

            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            _ = Interlocked.Decrement(ref _currentConcurrent);

            return default;
        }
    }

    private sealed record ValidatedCommand : ICommand<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }

        [Required]
        public string Name { get; init; } = string.Empty;
    }

    private sealed class ValidatedCommandHandler : ICommandHandler<ValidatedCommand, string>
    {
        public Task<string> HandleAsync(ValidatedCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult($"hello {command.Name}");
    }

    private sealed record ValidatedStreamQuery : IStreamQuery<string>
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }

        [Required]
        public string Name { get; init; } = string.Empty;
    }

    private sealed class ValidatedStreamQueryHandler : IStreamQueryHandler<ValidatedStreamQuery, string>
    {
        public async IAsyncEnumerable<string> HandleAsync(
            ValidatedStreamQuery request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return $"hello {request.Name}";
            await Task.Yield();
        }
    }

    private sealed record ValidatedEvent : IEvent
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }

        public required string Id { get; init; }

        public DateTimeOffset? PublishedAt { get; set; }

        [Required]
        public string Name { get; init; } = string.Empty;
    }

    private sealed class ValidatedEventHandler : IEventHandler<ValidatedEvent>
    {
        public static bool HandlerInvoked { get; private set; }

        public Task HandleAsync(ValidatedEvent message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            HandlerInvoked = true;
            return Task.CompletedTask;
        }
    }
}
