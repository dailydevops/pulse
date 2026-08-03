namespace NetEvolve.Pulse.Tests.Integration.Pipeline;

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Dispatchers;
using NetEvolve.Pulse.Extensibility;

/// <summary>
/// End-to-end integration tests exercising the core mediator's event dispatch pipeline
/// (dispatcher strategies, event filters, and assembly-scanning based handler registration)
/// through a real host built via <c>services.AddPulse(...)</c>. Unlike the Outbox tests in this
/// project, these tests do not require any database or broker container — <c>IMediator.PublishAsync</c>
/// invokes registered <see cref="IEventHandler{TEvent}"/> instances directly when no outbox is configured.
/// </summary>
[TestGroup("Pipeline")]
[Timeout(60_000)]
public sealed class EventDispatchingTests
{
    private static async Task RunAsync(
        Action<IServiceCollection> configureServices,
        Func<IServiceProvider, CancellationToken, Task> testableCode,
        CancellationToken cancellationToken
    )
    {
        using var host = new HostBuilder()
            .ConfigureServices(configureServices)
            .ConfigureWebHost(webBuilder => _ = webBuilder.UseTestServer().Configure(applicationBuilder => { }))
            .Build();

        await host.StartAsync(cancellationToken).ConfigureAwait(false);

        using var server = host.GetTestServer();

        var scope = server.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            await testableCode.Invoke(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Thread-safe recorder used by handlers across the tests in this file to prove ordering,
    /// concurrency, and filtering behaviour without relying on timing assumptions alone.
    /// </summary>
    private sealed class ExecutionTracker
    {
        private readonly ConcurrentQueue<string> _entries = new();

        public void Record(string name) => _entries.Enqueue(name);

        public IReadOnlyList<string> Entries => [.. _entries];
    }

    #region Sequential dispatcher

    private sealed class SequentialTestEvent : IEvent
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public string Id { get; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class SequentialHandlerA(ExecutionTracker tracker) : IEventHandler<SequentialTestEvent>
    {
        public async Task HandleAsync(SequentialTestEvent message, CancellationToken cancellationToken = default)
        {
            // Deliberately slower than the following handlers. Under sequential dispatch this
            // handler must still fully complete before the next one starts.
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            tracker.Record("A");
        }
    }

    private sealed class SequentialHandlerB(ExecutionTracker tracker) : IEventHandler<SequentialTestEvent>
    {
        public Task HandleAsync(SequentialTestEvent message, CancellationToken cancellationToken = default)
        {
            tracker.Record("B");
            return Task.CompletedTask;
        }
    }

    private sealed class SequentialHandlerC(ExecutionTracker tracker) : IEventHandler<SequentialTestEvent>
    {
        public Task HandleAsync(SequentialTestEvent message, CancellationToken cancellationToken = default)
        {
            tracker.Record("C");
            return Task.CompletedTask;
        }
    }

    [Test]
    public async Task SequentialDispatcher_Executes_Handlers_InRegistrationOrder(CancellationToken cancellationToken)
    {
        var tracker = new ExecutionTracker();

        await RunAsync(
                services =>
                    services
                        .AddSingleton(tracker)
                        .AddPulse(config =>
                            config
                                .UseDefaultEventDispatcher<SequentialEventDispatcher>()
                                .AddEventHandler<SequentialTestEvent, SequentialHandlerA>()
                                .AddEventHandler<SequentialTestEvent, SequentialHandlerB>()
                                .AddEventHandler<SequentialTestEvent, SequentialHandlerC>()
                        ),
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    await mediator.PublishAsync(new SequentialTestEvent(), token).ConfigureAwait(false);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(tracker.Entries).IsEquivalentTo(["A", "B", "C"]);
    }

    #endregion

    #region Parallel dispatcher

    private sealed class ParallelTestEvent : IEvent
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public string Id { get; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    /// <summary>
    /// Coordinates a rendezvous between two handlers: each signals its own gate and then waits for
    /// the other handler's gate. This only completes if both handlers are running concurrently -
    /// a purely sequential dispatcher would deadlock (and time out) here.
    /// </summary>
    private sealed class RendezvousGate
    {
        public TaskCompletionSource<bool> GateA { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> GateB { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ParallelHandlerA(RendezvousGate gate, ExecutionTracker tracker)
        : IEventHandler<ParallelTestEvent>
    {
        public async Task HandleAsync(ParallelTestEvent message, CancellationToken cancellationToken = default)
        {
            _ = gate.GateA.TrySetResult(true);
#pragma warning disable VSTHRD003 // rendezvous on a TaskCompletionSource signaled by a sibling handler, not a foreign task
            _ = await Task.WhenAny(gate.GateB.Task, Task.Delay(10_000, cancellationToken)).ConfigureAwait(false);
#pragma warning restore VSTHRD003
            tracker.Record("A");
        }
    }

    private sealed class ParallelHandlerB(RendezvousGate gate, ExecutionTracker tracker)
        : IEventHandler<ParallelTestEvent>
    {
        public async Task HandleAsync(ParallelTestEvent message, CancellationToken cancellationToken = default)
        {
            _ = gate.GateB.TrySetResult(true);
#pragma warning disable VSTHRD003 // rendezvous on a TaskCompletionSource signaled by a sibling handler, not a foreign task
            _ = await Task.WhenAny(gate.GateA.Task, Task.Delay(10_000, cancellationToken)).ConfigureAwait(false);
#pragma warning restore VSTHRD003
            tracker.Record("B");
        }
    }

    [Test]
    public async Task ParallelDispatcher_Executes_Handlers_Concurrently(CancellationToken cancellationToken)
    {
        var tracker = new ExecutionTracker();
        var gate = new RendezvousGate();

        await RunAsync(
                services =>
                    services
                        .AddSingleton(tracker)
                        .AddSingleton(gate)
                        .AddPulse(config =>
                            config
                                .UseDefaultEventDispatcher<ParallelEventDispatcher>()
                                .AddEventHandler<ParallelTestEvent, ParallelHandlerA>()
                                .AddEventHandler<ParallelTestEvent, ParallelHandlerB>()
                        ),
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    await mediator.PublishAsync(new ParallelTestEvent(), token).ConfigureAwait(false);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        // Both handlers must have unblocked each other and completed - proving concurrent execution.
        _ = await Assert.That(gate.GateA.Task.IsCompletedSuccessfully).IsTrue();
        _ = await Assert.That(gate.GateB.Task.IsCompletedSuccessfully).IsTrue();
        _ = await Assert.That(tracker.Entries.Count).IsEqualTo(2);
        _ = await Assert.That(tracker.Entries).Contains("A");
        _ = await Assert.That(tracker.Entries).Contains("B");
    }

    #endregion

    #region Prioritized dispatcher

    private sealed class PrioritizedTestEvent : IEvent
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public string Id { get; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class PrioritizedHighPriorityHandler(ExecutionTracker tracker)
        : IPrioritizedEventHandler<PrioritizedTestEvent>
    {
        public int Priority => 0;

        public async Task HandleAsync(PrioritizedTestEvent message, CancellationToken cancellationToken = default)
        {
            // Intentionally slower than the handlers that follow. Because priority groups execute
            // sequentially, this handler must still fully complete before the mid-priority group starts.
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            tracker.Record("High");
        }
    }

    private sealed class PrioritizedMidPriorityHandler(ExecutionTracker tracker)
        : IPrioritizedEventHandler<PrioritizedTestEvent>
    {
        public int Priority => 10;

        public Task HandleAsync(PrioritizedTestEvent message, CancellationToken cancellationToken = default)
        {
            tracker.Record("Mid");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Does not implement <see cref="IPrioritizedEventHandler{TEvent}"/>, so it is treated as
    /// priority <see cref="int.MaxValue"/> and must always execute last.
    /// </summary>
    private sealed class PrioritizedUnprioritizedHandler(ExecutionTracker tracker) : IEventHandler<PrioritizedTestEvent>
    {
        public Task HandleAsync(PrioritizedTestEvent message, CancellationToken cancellationToken = default)
        {
            tracker.Record("Unprioritized");
            return Task.CompletedTask;
        }
    }

    [Test]
    public async Task PrioritizedDispatcher_Executes_Handlers_InPriorityOrder(CancellationToken cancellationToken)
    {
        var tracker = new ExecutionTracker();

        await RunAsync(
                services =>
                    services
                        .AddSingleton(tracker)
                        .AddPulse(config =>
                            config
                                .UseDefaultEventDispatcher<PrioritizedEventDispatcher>()
                                .AddEventHandler<PrioritizedTestEvent, PrioritizedUnprioritizedHandler>()
                                .AddEventHandler<PrioritizedTestEvent, PrioritizedHighPriorityHandler>()
                                .AddEventHandler<PrioritizedTestEvent, PrioritizedMidPriorityHandler>()
                        ),
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    await mediator.PublishAsync(new PrioritizedTestEvent(), token).ConfigureAwait(false);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(tracker.Entries).IsEquivalentTo(["High", "Mid", "Unprioritized"]);
    }

    #endregion

    #region Rate-limited dispatcher

    private sealed class RateLimitedTestEvent : IEvent
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public string Id { get; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    /// <summary>
    /// Tracks the number of handlers currently executing, and the maximum observed concurrently,
    /// to verify that the <see cref="RateLimitedEventDispatcher"/> never exceeds its configured
    /// <see cref="RateLimitedEventDispatcher.MaxConcurrency"/>.
    /// </summary>
    private sealed class ConcurrencyTracker
    {
        private int _current;
        private int _max;

        public void Enter()
        {
            var current = Interlocked.Increment(ref _current);

            int observedMax;
            do
            {
                observedMax = _max;
                if (current <= observedMax)
                {
                    break;
                }
            } while (Interlocked.CompareExchange(ref _max, current, observedMax) != observedMax);
        }

        public void Exit() => Interlocked.Decrement(ref _current);

        public int MaxObserved => _max;
    }

    private sealed class RateLimitedHandler(ConcurrencyTracker tracker) : IEventHandler<RateLimitedTestEvent>
    {
        public async Task HandleAsync(RateLimitedTestEvent message, CancellationToken cancellationToken = default)
        {
            tracker.Enter();
            try
            {
                await Task.Delay(75, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                tracker.Exit();
            }
        }
    }

    [Test]
    public async Task RateLimitedDispatcher_Never_Exceeds_ConfiguredMaxConcurrency(CancellationToken cancellationToken)
    {
        const int maxConcurrency = 2;
        var tracker = new ConcurrencyTracker();

        await RunAsync(
                services =>
                    services
                        .AddSingleton(tracker)
                        .AddPulse(config =>
                            config
                                .UseDefaultEventDispatcher(_ => new RateLimitedEventDispatcher(maxConcurrency))
                                .AddEventHandler<RateLimitedTestEvent, RateLimitedHandler>()
                                .AddEventHandler<RateLimitedTestEvent, RateLimitedHandler>()
                                .AddEventHandler<RateLimitedTestEvent, RateLimitedHandler>()
                                .AddEventHandler<RateLimitedTestEvent, RateLimitedHandler>()
                                .AddEventHandler<RateLimitedTestEvent, RateLimitedHandler>()
                        ),
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    await mediator.PublishAsync(new RateLimitedTestEvent(), token).ConfigureAwait(false);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        using (Assert.Multiple())
        {
            // Never exceeded the configured limit ...
            _ = await Assert.That(tracker.MaxObserved).IsLessThanOrEqualTo(maxConcurrency);
            // ... but did reach it, proving handlers actually ran concurrently rather than sequentially.
            _ = await Assert.That(tracker.MaxObserved).IsEqualTo(maxConcurrency);
        }
    }

    #endregion

    #region Event filter / predicate

    private sealed class FilteredTestEvent : IEvent
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public string Id { get; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
        public required int Value { get; init; }
    }

    private sealed class FilteredEventHandler(ExecutionTracker tracker) : IEventHandler<FilteredTestEvent>
    {
        public Task HandleAsync(FilteredTestEvent message, CancellationToken cancellationToken = default)
        {
            tracker.Record($"Value:{message.Value}");
            return Task.CompletedTask;
        }
    }

    [Test]
    public async Task EventFilter_Predicate_Skips_Handler_For_FilteredEvents(CancellationToken cancellationToken)
    {
        var tracker = new ExecutionTracker();

        await RunAsync(
                services =>
                    services
                        .AddSingleton(tracker)
                        .AddPulse(config =>
                            config
                                .AddEventHandler<FilteredTestEvent, FilteredEventHandler>()
                                .AddEventFilter<FilteredTestEvent>((evt, _) => ValueTask.FromResult(evt.Value % 2 == 0))
                        ),
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    for (var i = 0; i < 5; i++)
                    {
                        await mediator.PublishAsync(new FilteredTestEvent { Value = i }, token).ConfigureAwait(false);
                    }
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(tracker.Entries).IsEquivalentTo(["Value:0", "Value:2", "Value:4"]);
    }

    #endregion

    #region Assembly scanning

    private sealed class ScannedTestEvent : IEvent
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public string Id { get; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class ScannedEventHandler(ExecutionTracker tracker) : IEventHandler<ScannedTestEvent>
    {
        public Task HandleAsync(ScannedTestEvent message, CancellationToken cancellationToken = default)
        {
            tracker.Record("Scanned");
            return Task.CompletedTask;
        }
    }

    [Test]
    public async Task AssemblyScanning_Discovers_And_Invokes_EventHandler(CancellationToken cancellationToken)
    {
        var tracker = new ExecutionTracker();

        await RunAsync(
                services =>
                    services
                        .AddSingleton(tracker)
                        .AddPulse(config => config.AddHandlersFromAssemblyContaining<EventDispatchingTests>()),
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    // Confirm the scanned handler type was actually the one discovered and registered
                    // by reflection, not merely a coincidentally-named handler.
                    var handlers = services.GetServices<IEventHandler<ScannedTestEvent>>().ToArray();
                    _ = await Assert.That(handlers).HasSingleItem();
                    _ = await Assert.That(handlers[0]).IsTypeOf<ScannedEventHandler>();

                    await mediator.PublishAsync(new ScannedTestEvent(), token).ConfigureAwait(false);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(tracker.Entries).IsEquivalentTo(["Scanned"]);
    }

    #endregion

    #region Per-event-type dispatcher (UseEventDispatcherFor)

    private sealed class KeyedTestEvent : IEvent
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public string Id { get; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private sealed class KeyedEventHandler(ExecutionTracker tracker) : IEventHandler<KeyedTestEvent>
    {
        public Task HandleAsync(KeyedTestEvent message, CancellationToken cancellationToken = default)
        {
            tracker.Record("Keyed");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Marker dispatcher distinct from the built-in strategies, so tests can prove the keyed
    /// registration actually routes to THIS dispatcher rather than the default one.
    /// </summary>
    private sealed class MarkerEventDispatcher(ExecutionTracker tracker) : IEventDispatcher
    {
        public async Task DispatchAsync<TEvent>(
            TEvent message,
            IEnumerable<IEventHandler<TEvent>> handlers,
            Func<IEventHandler<TEvent>, TEvent, CancellationToken, Task> invoker,
            CancellationToken cancellationToken
        )
            where TEvent : IEvent
        {
            tracker.Record("MarkerDispatcherUsed");
            foreach (var handler in handlers)
            {
                await invoker(handler, message, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    [Test]
    public async Task UseEventDispatcherFor_Routes_SpecificEventType_ToRegisteredDispatcher(
        CancellationToken cancellationToken
    )
    {
        var tracker = new ExecutionTracker();

        await RunAsync(
                services =>
                    services
                        .AddSingleton(tracker)
                        .AddSingleton<MarkerEventDispatcher>()
                        .AddSingleton<IEventHandler<KeyedTestEvent>, KeyedEventHandler>()
                        .AddPulse(config => config.UseEventDispatcherFor<KeyedTestEvent, MarkerEventDispatcher>()),
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    await mediator.PublishAsync(new KeyedTestEvent(), token).ConfigureAwait(false);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(tracker.Entries).IsEquivalentTo(["MarkerDispatcherUsed", "Keyed"]);
    }

    [Test]
    public async Task UseEventDispatcherFor_WithFactory_Routes_SpecificEventType_ToRegisteredDispatcher(
        CancellationToken cancellationToken
    )
    {
        var tracker = new ExecutionTracker();

        await RunAsync(
                services =>
                    services
                        .AddSingleton(tracker)
                        .AddSingleton<IEventHandler<KeyedTestEvent>, KeyedEventHandler>()
                        .AddPulse(config =>
                            config.UseEventDispatcherFor<KeyedTestEvent, MarkerEventDispatcher>(
                                sp => new MarkerEventDispatcher(sp.GetRequiredService<ExecutionTracker>())
                            )
                        ),
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    await mediator.PublishAsync(new KeyedTestEvent(), token).ConfigureAwait(false);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        _ = await Assert.That(tracker.Entries).IsEquivalentTo(["MarkerDispatcherUsed", "Keyed"]);
    }

    [Test]
    public async Task UseEventDispatcherFor_CalledTwice_ReplacesPreviousRegistration(
        CancellationToken cancellationToken
    )
    {
        var tracker = new ExecutionTracker();

        await RunAsync(
                services =>
                    services
                        .AddSingleton(tracker)
                        .AddSingleton<MarkerEventDispatcher>()
                        .AddSingleton<SequentialEventDispatcher>()
                        .AddSingleton<IEventHandler<KeyedTestEvent>, KeyedEventHandler>()
                        .AddPulse(config =>
                            config
                                .UseEventDispatcherFor<KeyedTestEvent, MarkerEventDispatcher>()
                                .UseEventDispatcherFor<KeyedTestEvent, SequentialEventDispatcher>()
                        ),
                async (services, token) =>
                {
                    var mediator = services.GetRequiredService<IMediator>();

                    await mediator.PublishAsync(new KeyedTestEvent(), token).ConfigureAwait(false);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        // Only the second (Sequential) registration should apply — the marker dispatcher must never run.
        _ = await Assert.That(tracker.Entries).IsEquivalentTo(["Keyed"]);
    }

    #endregion
}
