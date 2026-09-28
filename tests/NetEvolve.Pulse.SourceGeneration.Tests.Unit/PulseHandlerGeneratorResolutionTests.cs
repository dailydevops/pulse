namespace NetEvolve.Pulse.SourceGeneration.Tests.Unit;

using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using TUnit.Core;

/// <summary>
/// Emits the generated registration method together with the handlers, runs it on a <see cref="ServiceCollection"/>
/// after <c>AddPulse</c> and resolves the handlers from the built <see cref="ServiceProvider"/>.
/// Commands, queries and stream queries have exactly one handler, events can have several.
/// </summary>
[TestGroup("SourceGeneration")]
[TestGroup("SourceGeneration.PulseHandler")]
public class PulseHandlerGeneratorResolutionTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;
        using NetEvolve.Pulse.Extensibility;
        using NetEvolve.Pulse.Extensibility.Attributes;

        public abstract record RequestBase
        {
            public string? CausationId { get; set; }
            public string? CorrelationId { get; set; }
        }

        public record MyEvent : IEvent
        {
            public string Id { get; init; } = Guid.NewGuid().ToString();
            public string? CausationId { get; set; }
            public string? CorrelationId { get; set; }
            public DateTimeOffset? PublishedAt { get; set; }
        }

        public sealed record MyCommand(string Name) : RequestBase, ICommand<string>;

        public sealed record MyVoidCommand : RequestBase, ICommand;

        public sealed record MyQuery(int Id) : RequestBase, IQuery<int>;

        public sealed record MyStreamQuery : RequestBase, IStreamQuery<string>;

        public interface ILookupQuery;

        public sealed record LookupQuery : RequestBase, IQuery<string>, ILookupQuery;

        public sealed record ExplicitCommand : RequestBase, ICommand<string>;

        [PulseHandler]
        public class MyCommandHandler : ICommandHandler<MyCommand, string>
        {
            public Task<string> HandleAsync(MyCommand command, CancellationToken cancellationToken = default)
                => Task.FromResult(command.Name);
        }

        [PulseHandler]
        public class MyVoidCommandHandler : ICommandHandler<MyVoidCommand, NetEvolve.Pulse.Extensibility.Void>
        {
            public Task<NetEvolve.Pulse.Extensibility.Void> HandleAsync(
                MyVoidCommand command,
                CancellationToken cancellationToken = default
            ) => Task.FromResult(NetEvolve.Pulse.Extensibility.Void.Completed);
        }

        [PulseHandler(Lifetime = PulseServiceLifetime.Singleton)]
        public class MyQueryHandler : IQueryHandler<MyQuery, int>
        {
            public Task<int> HandleAsync(MyQuery request, CancellationToken cancellationToken = default)
                => Task.FromResult(request.Id);
        }

        [PulseHandler(Lifetime = PulseServiceLifetime.Transient)]
        public class MyStreamQueryHandler : IStreamQueryHandler<MyStreamQuery, string>
        {
            public async IAsyncEnumerable<string> HandleAsync(
                MyStreamQuery request,
                [EnumeratorCancellation] CancellationToken cancellationToken = default
            )
            {
                await Task.Yield();
                yield return "item";
            }
        }

        [PulseHandler]
        public class EventHandlerA : IEventHandler<MyEvent>
        {
            public Task HandleAsync(MyEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }

        [PulseHandler(Lifetime = PulseServiceLifetime.Singleton)]
        public class EventHandlerB : IEventHandler<MyEvent>
        {
            public Task HandleAsync(MyEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }

        [PulseHandler]
        public class PrioritizedEventHandler : IPrioritizedEventHandler<MyEvent>
        {
            public int Priority => 1;

            public Task HandleAsync(MyEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }

        [PulseGenericHandler]
        public class GenericEventHandler<TEvent> : IEventHandler<TEvent>
            where TEvent : IEvent
        {
            public Task HandleAsync(TEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }

        [PulseGenericHandler]
        public class GenericLookupHandler<TQuery, TResult> : IQueryHandler<TQuery, TResult>
            where TQuery : IQuery<TResult>, ILookupQuery
        {
            public Task<TResult> HandleAsync(TQuery request, CancellationToken cancellationToken = default)
                => Task.FromResult(default(TResult)!);
        }

        [PulseHandler<ExplicitCommand>]
        public class ExplicitCommandHandler<TCommand, TResult> : ICommandHandler<TCommand, TResult>
            where TCommand : ICommand<TResult>
        {
            public Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default)
                => Task.FromResult(default(TResult)!);
        }

        public static class Outer
        {
            [PulseHandler]
            public sealed class NestedQueryHandler : IQueryHandler<NestedQuery, string>
            {
                public Task<string> HandleAsync(NestedQuery request, CancellationToken cancellationToken = default)
                    => Task.FromResult("nested");
            }

            public sealed record NestedQuery : RequestBase, IQuery<string>;
        }
        """;

    private const string DuplicateCommandHandlerSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using NetEvolve.Pulse.Extensibility;
        using NetEvolve.Pulse.Extensibility.Attributes;

        public sealed record MyCommand : ICommand<string>
        {
            public string? CausationId { get; set; }
            public string? CorrelationId { get; set; }
        }

        [PulseHandler]
        public class FirstHandler : ICommandHandler<MyCommand, string>
        {
            public Task<string> HandleAsync(MyCommand command, CancellationToken cancellationToken = default)
                => Task.FromResult("first");
        }

        [PulseHandler]
        public class SecondHandler : ICommandHandler<MyCommand, string>
        {
            public Task<string> HandleAsync(MyCommand command, CancellationToken cancellationToken = default)
                => Task.FromResult("second");
        }
        """;

    [Test]
    public async Task WhenAllHandlerKindsAnnotatedThenGeneratedCodeCompilesWithoutPulseDiagnostics()
    {
        var run = GeneratorHarness.Run(Source, referencePulse: true);

        using (Assert.Multiple())
        {
            _ = await Assert.That(run.PulseDiagnostics).IsEmpty();
            _ = await Assert.That(run.InputErrors).IsEmpty();
            _ = await Assert.That(run.GeneratedErrors).IsEmpty();
        }
    }

    [Test]
    public async Task WhenCommandHandlerAnnotatedThenContainerResolvesIt()
    {
        using var assembly = Load(Source);
        await using var provider = BuildServiceProvider(assembly);
        await using var scope = provider.CreateAsyncScope();

        var handlers = ResolveAll(scope.ServiceProvider, assembly, typeof(ICommandHandler<,>), "MyCommand", "string");

        _ = await Assert.That(handlers).IsEquivalentTo(["MyCommandHandler"]);
    }

    [Test]
    public async Task WhenVoidCommandHandlerAnnotatedThenContainerResolvesIt()
    {
        using var assembly = Load(Source);
        await using var provider = BuildServiceProvider(assembly);
        await using var scope = provider.CreateAsyncScope();

        var handlers = ResolveAll(scope.ServiceProvider, assembly, typeof(ICommandHandler<,>), "MyVoidCommand", "void");

        _ = await Assert.That(handlers).IsEquivalentTo(["MyVoidCommandHandler"]);
    }

    [Test]
    public async Task WhenQueryHandlerAnnotatedAsSingletonThenContainerResolvesOneInstance()
    {
        using var assembly = Load(Source);
        await using var provider = BuildServiceProvider(assembly);
        var serviceType = Close(assembly, typeof(IQueryHandler<,>), "MyQuery", "int");
        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();

        var handler = first.ServiceProvider.GetRequiredService(serviceType);

        using (Assert.Multiple())
        {
            _ = await Assert.That(ResolveAll(first.ServiceProvider, serviceType)).IsEquivalentTo(["MyQueryHandler"]);
            _ = await Assert.That(second.ServiceProvider.GetRequiredService(serviceType)).IsSameReferenceAs(handler);
        }
    }

    [Test]
    public async Task WhenStreamQueryHandlerAnnotatedAsTransientThenContainerResolvesNewInstances()
    {
        using var assembly = Load(Source);
        await using var provider = BuildServiceProvider(assembly);
        var serviceType = Close(assembly, typeof(IStreamQueryHandler<,>), "MyStreamQuery", "string");
        await using var scope = provider.CreateAsyncScope();

        var handler = scope.ServiceProvider.GetRequiredService(serviceType);

        using (Assert.Multiple())
        {
            _ = await Assert
                .That(ResolveAll(scope.ServiceProvider, serviceType))
                .IsEquivalentTo(["MyStreamQueryHandler"]);
            _ = await Assert.That(scope.ServiceProvider.GetRequiredService(serviceType)).IsNotSameReferenceAs(handler);
        }
    }

    [Test]
    public async Task WhenNestedQueryHandlerAnnotatedThenContainerResolvesIt()
    {
        using var assembly = Load(Source);
        await using var provider = BuildServiceProvider(assembly);
        await using var scope = provider.CreateAsyncScope();

        var handlers = ResolveAll(
            scope.ServiceProvider,
            assembly,
            typeof(IQueryHandler<,>),
            "Outer+NestedQuery",
            "string"
        );

        _ = await Assert.That(handlers).IsEquivalentTo(["NestedQueryHandler"]);
    }

    [Test]
    public async Task WhenSeveralEventHandlersAnnotatedThenContainerResolvesAllOfThem()
    {
        using var assembly = Load(Source);
        await using var provider = BuildServiceProvider(assembly);
        await using var scope = provider.CreateAsyncScope();

        var handlers = ResolveAll(scope.ServiceProvider, assembly, typeof(IEventHandler<>), "MyEvent");

        _ = await Assert
            .That(handlers)
            .IsEquivalentTo(["EventHandlerA", "EventHandlerB", "GenericEventHandler`1", "PrioritizedEventHandler"]);
    }

    [Test]
    public async Task WhenOpenGenericQueryHandlerAnnotatedThenContainerResolvesClosedHandler()
    {
        using var assembly = Load(Source);
        await using var provider = BuildServiceProvider(assembly);
        await using var scope = provider.CreateAsyncScope();

        var handler = scope.ServiceProvider.GetRequiredService(
            Close(assembly, typeof(IQueryHandler<,>), "LookupQuery", "string")
        );

        _ = await Assert
            .That(handler.GetType())
            .IsEqualTo(
                assembly
                    .GetTypeByName("GenericLookupHandler`2")
                    .MakeGenericType(assembly.GetTypeByName("LookupQuery"), typeof(string))
            );
    }

    [Test]
    public async Task WhenGenericHandlerAnnotatedWithExplicitMessageTypeThenContainerResolvesClosedHandler()
    {
        using var assembly = Load(Source);
        await using var provider = BuildServiceProvider(assembly);
        await using var scope = provider.CreateAsyncScope();

        var handler = scope.ServiceProvider.GetRequiredService(
            Close(assembly, typeof(ICommandHandler<,>), "ExplicitCommand", "string")
        );

        _ = await Assert
            .That(handler.GetType())
            .IsEqualTo(
                assembly
                    .GetTypeByName("ExplicitCommandHandler`2")
                    .MakeGenericType(assembly.GetTypeByName("ExplicitCommand"), typeof(string))
            );
    }

    [Test]
    public async Task WhenTwoCommandHandlersAnnotatedThenPulse002ReportedAndOnlyFirstIsResolved()
    {
        var run = GeneratorHarness.Run(DuplicateCommandHandlerSource, referencePulse: true).EnsureCompiles();
        using var assembly = run.Load();
        await using var provider = BuildServiceProvider(assembly);
        await using var scope = provider.CreateAsyncScope();

        var handlers = ResolveAll(scope.ServiceProvider, assembly, typeof(ICommandHandler<,>), "MyCommand", "string");

        using (Assert.Multiple())
        {
            _ = await Assert.That(run.PulseDiagnostics.Select(d => d.Id)).IsEquivalentTo(["PULSE002"]);
            _ = await Assert.That(handlers).IsEquivalentTo(["FirstHandler"]);
        }
    }

    private static LoadedAssembly Load(string source) =>
        GeneratorHarness.Run(source, referencePulse: true).EnsureCompiles().Load();

    private static ServiceProvider BuildServiceProvider(LoadedAssembly assembly)
    {
        var services = new ServiceCollection().AddLogging().AddPulse();
        _ = assembly.AddPulseHandlers(services);

        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );
    }

    private static Type Close(LoadedAssembly assembly, Type openServiceType, params string[] typeArguments) =>
        openServiceType.MakeGenericType([
            .. typeArguments.Select(name =>
                name switch
                {
                    "string" => typeof(string),
                    "int" => typeof(int),
                    "void" => typeof(Extensibility.Void),
                    _ => assembly.GetTypeByName(name),
                }
            ),
        ]);

    private static string[] ResolveAll(
        IServiceProvider services,
        LoadedAssembly assembly,
        Type openServiceType,
        params string[] typeArguments
    ) => ResolveAll(services, Close(assembly, openServiceType, typeArguments));

    private static string[] ResolveAll(IServiceProvider services, Type serviceType) =>
        [.. services.GetServices(serviceType).Select(handler => handler!.GetType().Name)];
}
