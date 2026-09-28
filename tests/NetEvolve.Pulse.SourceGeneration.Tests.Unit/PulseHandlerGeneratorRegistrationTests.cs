namespace NetEvolve.Pulse.SourceGeneration.Tests.Unit;

using System.Linq;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using TUnit.Core;

/// <summary>
/// Compiles the generated registration method together with the handlers and runs it against the DI container,
/// so the tests verify which handlers the container actually resolves.
/// </summary>
[TestGroup("SourceGeneration")]
[TestGroup("SourceGeneration.PulseHandler")]
public class PulseHandlerGeneratorRegistrationTests
{
    private const string Source = """
        using System;
        using System.Collections.Concurrent;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;
        using NetEvolve.Pulse;
        using NetEvolve.Pulse.Extensibility;
        using NetEvolve.Pulse.Extensibility.Attributes;
        using TestAssembly;

        public record MyEvent : IEvent
        {
            public string Id { get; init; } = Guid.NewGuid().ToString();
            public string? CausationId { get; set; }
            public string? CorrelationId { get; set; }
            public DateTimeOffset? PublishedAt { get; set; }
        }

        public record MyCommand(string Name) : ICommand<string>
        {
            public string? CausationId { get; set; }
            public string? CorrelationId { get; set; }
        }

        public static class Recorder
        {
            public static readonly ConcurrentQueue<string> Calls = new();
        }

        [PulseHandler]
        public class EventHandlerA : IEventHandler<MyEvent>
        {
            public Task HandleAsync(MyEvent message, CancellationToken cancellationToken = default)
            {
                Recorder.Calls.Enqueue(nameof(EventHandlerA));
                return Task.CompletedTask;
            }
        }

        [PulseHandler]
        public class EventHandlerB : IEventHandler<MyEvent>
        {
            public Task HandleAsync(MyEvent message, CancellationToken cancellationToken = default)
            {
                Recorder.Calls.Enqueue(nameof(EventHandlerB));
                return Task.CompletedTask;
            }
        }

        [PulseHandler]
        public class SharedHandler : ICommandHandler<MyCommand, string>, IEventHandler<MyEvent>
        {
            public Task<string> HandleAsync(MyCommand command, CancellationToken cancellationToken = default)
                => Task.FromResult(command.Name);

            public Task HandleAsync(MyEvent message, CancellationToken cancellationToken = default)
            {
                Recorder.Calls.Enqueue(nameof(SharedHandler));
                return Task.CompletedTask;
            }
        }

        [PulseGenericHandler]
        public class AuditEventHandler<TEvent> : IEventHandler<TEvent>
            where TEvent : IEvent
        {
            public Task HandleAsync(TEvent message, CancellationToken cancellationToken = default)
            {
                Recorder.Calls.Enqueue("AuditEventHandler");
                return Task.CompletedTask;
            }
        }

        public class ManualEventHandler : IEventHandler<MyEvent>
        {
            public Task HandleAsync(MyEvent message, CancellationToken cancellationToken = default)
            {
                Recorder.Calls.Enqueue(nameof(ManualEventHandler));
                return Task.CompletedTask;
            }
        }

        public static class Probe
        {
            public static string[] ResolveEventHandlers(bool registerTwice, bool addManualHandlerFirst)
            {
                var services = new ServiceCollection().AddLogging();
                _ = addManualHandlerFirst
                    ? services.AddPulse(config => config.AddEventHandler<MyEvent, ManualEventHandler>())
                    : services.AddPulse();
                _ = services.AddTestAssemblyPulseHandlers();
                if (registerTwice)
                {
                    _ = services.AddTestAssemblyPulseHandlers();
                }

                using var provider = services.BuildServiceProvider();
                using var scope = provider.CreateScope();
                return scope.ServiceProvider.GetServices<IEventHandler<MyEvent>>().Select(Name).OrderBy(x => x).ToArray();
            }

            public static bool SharedHandlerUsesOneInstance()
            {
                var services = new ServiceCollection().AddLogging().AddPulse().AddTestAssemblyPulseHandlers();

                using var provider = services.BuildServiceProvider();
                using var scope = provider.CreateScope();
                var shared = scope.ServiceProvider.GetRequiredService<SharedHandler>();
                return ReferenceEquals(shared, scope.ServiceProvider.GetRequiredService<ICommandHandler<MyCommand, string>>())
                    && scope.ServiceProvider.GetServices<IEventHandler<MyEvent>>().Contains(shared);
            }

            public static string[] OpenGenericEventHandlersAfterOutbox()
            {
                var services = new ServiceCollection().AddLogging().AddPulse(config => config.AddOutbox());
                _ = services.AddTestAssemblyPulseHandlers();

                return services
                    .Where(d => d.ServiceType == typeof(IEventHandler<>))
                    .Select(d => d.ImplementationType!.Name)
                    .OrderBy(x => x)
                    .ToArray();
            }

            public static async Task<string[]> PublishAsync()
            {
                var services = new ServiceCollection().AddLogging().AddPulse().AddTestAssemblyPulseHandlers();

                await using var provider = services.BuildServiceProvider();
                await using var scope = provider.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IMediator>().PublishAsync(new MyEvent());
                return Recorder.Calls.OrderBy(x => x).ToArray();
            }

            private static string Name(object handler) =>
                handler.GetType().IsGenericType ? "AuditEventHandler" : handler.GetType().Name;
        }
        """;

    [Test]
    public async Task WhenMultipleEventHandlersForSameEventThenAllAreResolved()
    {
        var handlers = (string[])InvokeProbe("ResolveEventHandlers", false, false);

        _ = await Assert
            .That(handlers)
            .IsEquivalentTo(["AuditEventHandler", "EventHandlerA", "EventHandlerB", "SharedHandler"]);
    }

    [Test]
    public async Task WhenRegistrationMethodCalledTwiceThenEventHandlersAreNotDuplicated()
    {
        var handlers = (string[])InvokeProbe("ResolveEventHandlers", true, false);

        _ = await Assert
            .That(handlers)
            .IsEquivalentTo(["AuditEventHandler", "EventHandlerA", "EventHandlerB", "SharedHandler"]);
    }

    [Test]
    public async Task WhenEventHandlerRegisteredManuallyFirstThenGeneratedEventHandlersAreAdded()
    {
        var handlers = (string[])InvokeProbe("ResolveEventHandlers", false, true);

        _ = await Assert
            .That(handlers)
            .IsEquivalentTo([
                "AuditEventHandler",
                "EventHandlerA",
                "EventHandlerB",
                "ManualEventHandler",
                "SharedHandler",
            ]);
    }

    [Test]
    public async Task WhenHandlerImplementsEventAndCommandHandlerThenOneInstanceServesBoth()
    {
        var sharesInstance = (bool)InvokeProbe("SharedHandlerUsesOneInstance");

        _ = await Assert.That(sharesInstance).IsTrue();
    }

    [Test]
    public async Task WhenOutboxRegisteredFirstThenOpenGenericEventHandlerIsAdded()
    {
        var handlers = (string[])InvokeProbe("OpenGenericEventHandlersAfterOutbox");

        _ = await Assert.That(handlers).IsEquivalentTo(["AuditEventHandler`1", "OutboxEventHandler`1"]);
    }

    [Test]
    public async Task WhenEventPublishedThenAllGeneratedEventHandlersAreInvoked()
    {
        var calls = await ((Task<string[]>)InvokeProbe("PublishAsync")).ConfigureAwait(false);

        _ = await Assert
            .That(calls)
            .IsEquivalentTo(["AuditEventHandler", "EventHandlerA", "EventHandlerB", "SharedHandler"]);
    }

    private static object InvokeProbe(string methodName, params object[] arguments)
    {
        using var assembly = GeneratorHarness.Run(Source, referencePulse: true).EnsureCompiles().Load();
        return assembly.GetTypeByName("Probe").GetMethod(methodName)!.Invoke(null, arguments)!;
    }
}
