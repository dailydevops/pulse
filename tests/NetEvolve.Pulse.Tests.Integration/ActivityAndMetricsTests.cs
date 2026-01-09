namespace NetEvolve.Pulse.Tests.Integration;

using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using TUnit.Core;
using Void = Extensibility.Void;

public sealed class ActivityAndMetricsTests
{
    private static ServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging().AddSingleton(TimeProvider.System);
        return services;
    }

    [Test]
    public async Task SendAsync_WithActivityAndMetrics_CreatesActivityAndRecordsMetrics()
    {
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "NetEvolve.Pulse",
            Sample = (ref _) => ActivitySamplingResult.AllData,
            ActivityStarted = _ => { },
            ActivityStopped = _ => { },
        };
        ActivitySource.AddActivityListener(activityListener);

        var services = new ServiceCollection().AddLogging();
        _ = services
            .AddPulse(config => config.AddActivityAndMetrics())
            .AddScoped<ICommandHandler<MetricsTestCommand, string>, MetricsTestCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var command = new MetricsTestCommand("TestValue");
        var result = await mediator.SendAsync<MetricsTestCommand, string>(command);

        _ = await Assert.That(result).IsEqualTo("TestValue");
    }

    [Test]
    public async Task QueryAsync_WithActivityAndMetrics_CreatesActivityAndRecordsMetrics()
    {
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "NetEvolve.Pulse",
            Sample = (ref _) => ActivitySamplingResult.AllData,
            ActivityStarted = _ => { },
            ActivityStopped = _ => { },
        };
        ActivitySource.AddActivityListener(activityListener);

        var services = CreateServiceCollection();
        _ = services
            .AddPulse(config => config.AddActivityAndMetrics())
            .AddScoped<IQueryHandler<MetricsTestQuery, string>, MetricsTestQueryHandler>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var query = new MetricsTestQuery("QueryValue");
        var result = await mediator.QueryAsync<MetricsTestQuery, string>(query);

        _ = await Assert.That(result).IsEqualTo("QueryValue");
    }

    [Test]
    public async Task PublishAsync_WithActivityAndMetrics_CreatesActivityAndRecordsMetrics()
    {
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "NetEvolve.Pulse",
            Sample = (ref _) => ActivitySamplingResult.AllData,
            ActivityStarted = _ => { },
            ActivityStopped = _ => { },
        };
        ActivitySource.AddActivityListener(activityListener);

        var services = CreateServiceCollection();
        _ = services
            .AddPulse(config => config.AddActivityAndMetrics())
            .AddScoped<IEventHandler<MetricsTestEvent>, MetricsTestEventHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var handler = scope.ServiceProvider.GetService<IEventHandler<MetricsTestEvent>>() as MetricsTestEventHandler;

        var evt = new MetricsTestEvent("EventValue");
        await mediator.PublishAsync(evt);

        await Task.Delay(50);

        _ = await Assert.That(handler).IsNotNull();
        _ = await Assert.That(handler!.Handled).IsTrue();
    }

    [Test]
    [NotInParallel]
    public async Task SendAsync_WithActivityAndMetrics_PropagatesActivityContext()
    {
        Activity? capturedActivity = null;

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "NetEvolve.Pulse",
            Sample = (ref _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => capturedActivity = activity,
            ActivityStopped = _ => { },
        };
        ActivitySource.AddActivityListener(activityListener);

        var services = CreateServiceCollection();
        _ = services
            .AddPulse(config => config.AddActivityAndMetrics())
            .AddScoped<ICommandHandler<MetricsTestCommand, string>, MetricsTestCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var command = new MetricsTestCommand("Context");
        _ = await mediator.SendAsync<MetricsTestCommand, string>(command);

        using (Assert.Multiple())
        {
            _ = await Assert.That(capturedActivity).IsNotNull();
            _ = await Assert.That(capturedActivity!.DisplayName).Contains("MetricsTestCommand");
        }
    }

    [Test]
    public async Task SendAsync_WithException_RecordsMetricsWithErrorStatus()
    {
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "NetEvolve.Pulse",
            Sample = (ref _) => ActivitySamplingResult.AllData,
            ActivityStarted = _ => { },
            ActivityStopped = _ => { },
        };
        ActivitySource.AddActivityListener(activityListener);

        var services = CreateServiceCollection();
        _ = services
            .AddPulse(config => config.AddActivityAndMetrics())
            .AddScoped<ICommandHandler<FailingCommand, Void>, FailingCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var command = new FailingCommand();

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await mediator.SendAsync<FailingCommand, Void>(command)
        );
    }

    [Test]
    public async Task SendAsync_WithActivityAndMetrics_HandlesMultipleConcurrentRequests()
    {
        const int numberOfRequests = 100;

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "NetEvolve.Pulse",
            Sample = (ref _) => ActivitySamplingResult.AllData,
            ActivityStarted = _ => { },
            ActivityStopped = _ => { },
        };
        ActivitySource.AddActivityListener(activityListener);

        var services = CreateServiceCollection();
        _ = services
            .AddPulse(config => config.AddActivityAndMetrics())
            .AddScoped<ICommandHandler<MetricsTestCommand, string>, MetricsTestCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var tasks = Enumerable
            .Range(0, numberOfRequests)
            .Select(i => mediator.SendAsync<MetricsTestCommand, string>(new MetricsTestCommand($"Concurrent{i}")))
            .ToList();

        var results = await Task.WhenAll(tasks);

        _ = await Assert.That(results.Length).IsEqualTo(numberOfRequests);
        for (var i = 0; i < numberOfRequests; i++)
        {
            _ = await Assert.That(results[i]).IsEqualTo($"Concurrent{i}");
        }
    }

    private sealed record MetricsTestCommand(string Value) : ICommand<string>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record MetricsTestQuery(string Value) : IQuery<string>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record FailingCommand : ICommand<Void>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed class MetricsTestEvent : IEvent
    {
        public MetricsTestEvent(string value) => Value = value;

        public string? CorrelationId { get; set; }
        public string Id { get; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
        public string Value { get; }
    }

    private sealed class MetricsTestCommandHandler : ICommandHandler<MetricsTestCommand, string>
    {
        public Task<string> HandleAsync(MetricsTestCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult(command.Value);
    }

    private sealed class MetricsTestQueryHandler : IQueryHandler<MetricsTestQuery, string>
    {
        public Task<string> HandleAsync(MetricsTestQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(query.Value);
    }

    private sealed class MetricsTestEventHandler : IEventHandler<MetricsTestEvent>
    {
        public bool Handled { get; private set; }

        public Task HandleAsync(MetricsTestEvent message, CancellationToken cancellationToken = default)
        {
            Handled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingCommandHandler : ICommandHandler<FailingCommand, Void>
    {
        public Task<Void> HandleAsync(FailingCommand command, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Command failed intentionally");
    }
}
