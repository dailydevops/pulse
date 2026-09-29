namespace NetEvolve.Pulse.Tests.Unit.Internals;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Dispatchers;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Internals;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

/// <summary>
/// Verifies how <see cref="PulseMediator"/> dispatches the framework's <see cref="OutboxEventHandler{TEvent}"/>
/// next to user handlers that share the caller's scoped services (e.g. the same <c>DbContext</c>).
/// </summary>
[TestGroup("Internals")]
public class PulseMediatorOutboxDispatchTests
{
    [Test]
    public async Task PublishAsync_WithOutboxAndScopedHandler_DefaultDispatcher_NeverUsesSharedDependencyConcurrently(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var services = CreateServices();
        _ = services.AddScoped<IEventHandler<TestEvent>, SharedDependencyHandler>();
        var serviceProvider = services.BuildServiceProvider();

        var scope = serviceProvider.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = CreateMediator(scope.ServiceProvider);
            var shared = scope.ServiceProvider.GetRequiredService<SharedDependency>();

            await mediator.PublishAsync(new TestEvent(), cancellationToken).ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(string.Join(",", shared.Calls)).IsEqualTo("outbox,handler");
                _ = await Assert.That(shared.MaxConcurrency).IsEqualTo(1);
            }
        }
    }

    [Test]
    public async Task PublishAsync_WithOutboxAndSingleHandler_PassesOnlyUserHandlerToDispatcher(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dispatcher = new RecordingDispatcher();
        var services = CreateServices();
        _ = services.AddScoped<IEventHandler<TestEvent>, SharedDependencyHandler>();
        var serviceProvider = services.BuildServiceProvider();

        var scope = serviceProvider.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = CreateMediator(scope.ServiceProvider, dispatcher);
            var shared = scope.ServiceProvider.GetRequiredService<SharedDependency>();

            await mediator.PublishAsync(new TestEvent(), cancellationToken).ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(dispatcher.DispatchedHandlers).HasSingleItem();
                _ = await Assert.That(dispatcher.DispatchedHandlers[0]).IsTypeOf<SharedDependencyHandler>();
                _ = await Assert.That(string.Join(",", shared.Calls)).IsEqualTo("outbox,handler");
            }
        }
    }

    [Test]
    public async Task PublishAsync_WithOnlyOutboxHandler_StoresEventWithoutDispatcher(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dispatcher = new RecordingDispatcher();
        var serviceProvider = CreateServices().BuildServiceProvider();

        var scope = serviceProvider.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = CreateMediator(scope.ServiceProvider, dispatcher);
            var shared = scope.ServiceProvider.GetRequiredService<SharedDependency>();

            await mediator.PublishAsync(new TestEvent(), cancellationToken).ConfigureAwait(false);

            using (Assert.Multiple())
            {
                _ = await Assert.That(string.Join(",", shared.Calls)).IsEqualTo("outbox");
                _ = await Assert.That(dispatcher.DispatchedHandlers).IsEmpty();
            }
        }
    }

    [Test]
    public async Task PublishAsync_WithFailingOutbox_StillRunsOtherHandlersAndThrowsFlatAggregate(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var services = CreateServices();
        _ = services.AddScoped<IEventHandler<TestEvent>, SharedDependencyHandler>();
        _ = services.AddScoped<IEventHandler<TestEvent>, ThrowingHandler>();
        var serviceProvider = services.BuildServiceProvider();

        var scope = serviceProvider.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = CreateMediator(scope.ServiceProvider, new SequentialEventDispatcher());
            var shared = scope.ServiceProvider.GetRequiredService<SharedDependency>();
            shared.FailOutbox = true;

            var exception = await Assert.ThrowsAsync<AggregateException>(async () =>
                await mediator.PublishAsync(new TestEvent(), cancellationToken).ConfigureAwait(false)
            );

            using (Assert.Multiple())
            {
                _ = await Assert.That(exception!.InnerExceptions).Count().IsEqualTo(2);
                _ = await Assert.That(exception.InnerExceptions).All(x => x is InvalidOperationException);
                _ = await Assert.That(shared.Calls).Contains("handler");
            }
        }
    }

    [Test]
    public async Task PublishAsync_WithSuppressingInterceptor_DoesNotStoreOutboxMessage(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var services = CreateServices();
        _ = services.AddScoped<IEventHandler<TestEvent>, SharedDependencyHandler>();
        _ = services.AddSingleton<IEventInterceptor<TestEvent>, SuppressingInterceptor>();
        var serviceProvider = services.BuildServiceProvider();

        var scope = serviceProvider.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var mediator = CreateMediator(scope.ServiceProvider);
            var shared = scope.ServiceProvider.GetRequiredService<SharedDependency>();

            await mediator.PublishAsync(new TestEvent(), cancellationToken).ConfigureAwait(false);

            _ = await Assert.That(shared.Calls).IsEmpty();
        }
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddScoped<SharedDependency>();
        _ = services.AddScoped<IEventOutbox, SharedDependencyOutbox>();
        _ = services.AddScoped<IEventHandler<TestEvent>, OutboxEventHandler<TestEvent>>();
        return services;
    }

    private static PulseMediator CreateMediator(
        IServiceProvider serviceProvider,
        IEventDispatcher? dispatcher = null
    ) =>
        new(
            serviceProvider.GetRequiredService<ILogger<PulseMediator>>(),
            serviceProvider,
            TimeProvider.System,
            dispatcher
        );

    /// <summary>
    /// Stands in for a scoped <c>DbContext</c>: records who used it and how many callers used it at once.
    /// </summary>
    private sealed class SharedDependency
    {
        private int _active;
        private int _maxConcurrency;

        public ConcurrentQueue<string> Calls { get; } = new();

        public int MaxConcurrency => Volatile.Read(ref _maxConcurrency);

        public bool FailOutbox { get; set; }

        public async Task UseAsync(string caller, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var active = Interlocked.Increment(ref _active);
            try
            {
                int current;
                do
                {
                    current = Volatile.Read(ref _maxConcurrency);
                } while (
                    active > current && Interlocked.CompareExchange(ref _maxConcurrency, active, current) != current
                );

                Calls.Enqueue(caller);

                // Hold the "context" long enough for an overlapping caller to observe it.
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _ = Interlocked.Decrement(ref _active);
            }
        }
    }

    [SuppressMessage(
        "Major Code Smell",
        "S1144:Unused private types or members should be removed",
        Justification = "Resolved through dependency injection."
    )]
    private sealed class SharedDependencyOutbox(SharedDependency shared) : IEventOutbox
    {
        public async Task StoreAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default)
            where TEvent : IEvent
        {
            cancellationToken.ThrowIfCancellationRequested();

            await shared.UseAsync("outbox", cancellationToken).ConfigureAwait(false);

            if (shared.FailOutbox)
            {
                throw new InvalidOperationException("Outbox failure");
            }
        }
    }

    [SuppressMessage(
        "Major Code Smell",
        "S1144:Unused private types or members should be removed",
        Justification = "Resolved through dependency injection."
    )]
    private sealed class SharedDependencyHandler(SharedDependency shared) : IEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return shared.UseAsync("handler", cancellationToken);
        }
    }

    private sealed class ThrowingHandler : IEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            throw new InvalidOperationException("Handler failure");
        }
    }

    private sealed class SuppressingInterceptor : IEventInterceptor<TestEvent>
    {
        public Task HandleAsync(
            TestEvent message,
            Func<TestEvent, CancellationToken, Task> handler,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDispatcher : IEventDispatcher
    {
        public List<object> DispatchedHandlers { get; } = [];

        public async Task DispatchAsync<TEvent>(
            TEvent message,
            IEnumerable<IEventHandler<TEvent>> handlers,
            Func<IEventHandler<TEvent>, TEvent, CancellationToken, Task> invoker,
            CancellationToken cancellationToken
        )
            where TEvent : IEvent
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var handler in handlers)
            {
                DispatchedHandlers.Add(handler);
                await invoker(handler, message, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed class TestEvent : IEvent
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();

        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }

        public DateTimeOffset? PublishedAt { get; set; }
    }
}
