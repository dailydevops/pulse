namespace NetEvolve.Pulse.SourceGeneration.Tests.Unit;

using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using TUnit.Core;

public class PulseHandlerGeneratorTests
{
    [Test]
    public async Task WhenCommandHandlerAnnotatedThenRegistrationIsGenerated()
    {
        var source = """
            using NetEvolve.Pulse.SourceGeneration;
            using NetEvolve.Pulse.Extensibility;
            using System.Threading;
            using System.Threading.Tasks;

            public record MyCommand(string Name) : ICommand<string>;

            [PulseHandler]
            public class MyCommandHandler : ICommandHandler<MyCommand, string>
            {
                public Task<string> HandleAsync(MyCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult(command.Name);
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);

        _ = await Assert.That(diagnostics).IsEmpty();
        _ = await Assert.That(generatedSources).Count().IsEqualTo(1);

        var generatedCode = generatedSources[0].SourceText.ToString();
        _ = await Assert.That(generatedCode).Contains("AddGeneratedPulseHandlers");
        _ = await Assert.That(generatedCode).Contains("TryAddScoped");
        _ = await Assert.That(generatedCode).Contains("ICommandHandler");
        _ = await Assert.That(generatedCode).Contains("MyCommandHandler");
    }

    [Test]
    public async Task WhenQueryHandlerAnnotatedThenRegistrationIsGenerated()
    {
        var source = """
            using NetEvolve.Pulse.SourceGeneration;
            using NetEvolve.Pulse.Extensibility;
            using System.Threading;
            using System.Threading.Tasks;

            public record MyQuery(string Id) : IQuery<string>;

            [PulseHandler]
            public class MyQueryHandler : IQueryHandler<MyQuery, string>
            {
                public Task<string> HandleAsync(MyQuery request, CancellationToken cancellationToken = default)
                    => Task.FromResult(request.Id);
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);

        _ = await Assert.That(diagnostics).IsEmpty();
        _ = await Assert.That(generatedSources).Count().IsEqualTo(1);

        var generatedCode = generatedSources[0].SourceText.ToString();
        _ = await Assert.That(generatedCode).Contains("AddGeneratedPulseHandlers");
        _ = await Assert.That(generatedCode).Contains("TryAddScoped");
        _ = await Assert.That(generatedCode).Contains("IQueryHandler");
        _ = await Assert.That(generatedCode).Contains("MyQueryHandler");
    }

    [Test]
    public async Task WhenEventHandlerAnnotatedThenRegistrationIsGenerated()
    {
        var source = """
            using NetEvolve.Pulse.SourceGeneration;
            using NetEvolve.Pulse.Extensibility;
            using System.Threading;
            using System.Threading.Tasks;
            using System;

            public record MyEvent : IEvent
            {
                public string Id { get; init; } = Guid.NewGuid().ToString();
                public string? CorrelationId { get; set; }
                public DateTimeOffset? PublishedAt { get; set; }
            }

            [PulseHandler]
            public class MyEventHandler : IEventHandler<MyEvent>
            {
                public Task HandleAsync(MyEvent message, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);

        _ = await Assert.That(diagnostics).IsEmpty();
        _ = await Assert.That(generatedSources).Count().IsEqualTo(1);

        var generatedCode = generatedSources[0].SourceText.ToString();
        _ = await Assert.That(generatedCode).Contains("AddGeneratedPulseHandlers");
        _ = await Assert.That(generatedCode).Contains("TryAddScoped");
        _ = await Assert.That(generatedCode).Contains("IEventHandler");
        _ = await Assert.That(generatedCode).Contains("MyEventHandler");
    }

    [Test]
    public async Task WhenMultipleInterfacesImplementedThenAllRegistered()
    {
        var source = """
            using NetEvolve.Pulse.SourceGeneration;
            using NetEvolve.Pulse.Extensibility;
            using System.Threading;
            using System.Threading.Tasks;
            using System;

            public record MyCommand(string Name) : ICommand<string>;

            public record MyEvent : IEvent
            {
                public string Id { get; init; } = Guid.NewGuid().ToString();
                public string? CorrelationId { get; set; }
                public DateTimeOffset? PublishedAt { get; set; }
            }

            [PulseHandler]
            public class MultiHandler : ICommandHandler<MyCommand, string>, IEventHandler<MyEvent>
            {
                public Task<string> HandleAsync(MyCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult(command.Name);

                public Task HandleAsync(MyEvent message, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);

        _ = await Assert.That(diagnostics).IsEmpty();
        _ = await Assert.That(generatedSources).Count().IsEqualTo(1);

        var generatedCode = generatedSources[0].SourceText.ToString();
        _ = await Assert.That(generatedCode).Contains("ICommandHandler");
        _ = await Assert.That(generatedCode).Contains("IEventHandler");
        _ = await Assert.That(generatedCode).Contains("MultiHandler");
    }

    [Test]
    public async Task WhenNoHandlerInterfaceImplementedThenPulse001Reported()
    {
        var source = """
            using NetEvolve.Pulse.SourceGeneration;

            [PulseHandler]
            public class NotAHandler
            {
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        _ = await Assert.That(diagnostics).Count().IsEqualTo(1);

        _ = await Assert.That(diagnostics[0].Id).IsEqualTo("PULSE001");
        _ = await Assert.That(diagnostics[0].Severity).IsEqualTo(DiagnosticSeverity.Error);
    }

    [Test]
    public async Task WhenNoHandlerInterfaceImplementedThenPulse001MessageContainsTypeName()
    {
        var source = """
            using NetEvolve.Pulse.SourceGeneration;

            [PulseHandler]
            public class NotAHandler
            {
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        _ = await Assert.That(diagnostics).Count().IsEqualTo(1);

        var message = diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        _ = await Assert.That(message).Contains("NotAHandler");
    }

    [Test]
    public async Task WhenDuplicateCommandHandlersThenPulse002Reported()
    {
        var source = """
            using NetEvolve.Pulse.SourceGeneration;
            using NetEvolve.Pulse.Extensibility;
            using System.Threading;
            using System.Threading.Tasks;

            public record MyCommand(string Name) : ICommand<string>;

            [PulseHandler]
            public class HandlerA : ICommandHandler<MyCommand, string>
            {
                public Task<string> HandleAsync(MyCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult("A");
            }

            [PulseHandler]
            public class HandlerB : ICommandHandler<MyCommand, string>
            {
                public Task<string> HandleAsync(MyCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult("B");
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);

        // Should still generate registrations but also report the warning.
        _ = await Assert.That(generatedSources).Count().IsEqualTo(1);

        var warningDiagnostics = diagnostics.Where(d => d.Id == "PULSE002").ToImmutableArray();

        _ = await Assert.That(warningDiagnostics).Count().IsEqualTo(1);
        _ = await Assert.That(warningDiagnostics[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task WhenDuplicateCommandHandlersThenPulse002MessageContainsBothHandlerNames()
    {
        var source = """
            using NetEvolve.Pulse.SourceGeneration;
            using NetEvolve.Pulse.Extensibility;
            using System.Threading;
            using System.Threading.Tasks;

            public record MyCommand(string Name) : ICommand<string>;

            [PulseHandler]
            public class HandlerA : ICommandHandler<MyCommand, string>
            {
                public Task<string> HandleAsync(MyCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult("A");
            }

            [PulseHandler]
            public class HandlerB : ICommandHandler<MyCommand, string>
            {
                public Task<string> HandleAsync(MyCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult("B");
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        var warningDiagnostic = diagnostics.Single(d => d.Id == "PULSE002");
        var message = warningDiagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);

        _ = await Assert.That(message).Contains("HandlerA");
        _ = await Assert.That(message).Contains("HandlerB");
    }

    [Test]
    public async Task WhenDuplicateQueryHandlersThenPulse002Reported()
    {
        var source = """
            using NetEvolve.Pulse.SourceGeneration;
            using NetEvolve.Pulse.Extensibility;
            using System.Threading;
            using System.Threading.Tasks;

            public record MyQuery(string Id) : IQuery<string>;

            [PulseHandler]
            public class QueryHandlerA : IQueryHandler<MyQuery, string>
            {
                public Task<string> HandleAsync(MyQuery query, CancellationToken cancellationToken = default)
                    => Task.FromResult("A");
            }

            [PulseHandler]
            public class QueryHandlerB : IQueryHandler<MyQuery, string>
            {
                public Task<string> HandleAsync(MyQuery query, CancellationToken cancellationToken = default)
                    => Task.FromResult("B");
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        var warningDiagnostics = diagnostics.Where(d => d.Id == "PULSE002").ToImmutableArray();

        _ = await Assert.That(warningDiagnostics).Count().IsEqualTo(1);
        _ = await Assert.That(warningDiagnostics[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task WhenMultipleEventHandlersForSameEventThenNoPulse002()
    {
        var source = """
            using NetEvolve.Pulse.SourceGeneration;
            using NetEvolve.Pulse.Extensibility;
            using System.Threading;
            using System.Threading.Tasks;
            using System;

            public record MyEvent : IEvent
            {
                public string Id { get; init; } = Guid.NewGuid().ToString();
                public string? CorrelationId { get; set; }
                public DateTimeOffset? PublishedAt { get; set; }
            }

            [PulseHandler]
            public class EventHandlerA : IEventHandler<MyEvent>
            {
                public Task HandleAsync(MyEvent message, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }

            [PulseHandler]
            public class EventHandlerB : IEventHandler<MyEvent>
            {
                public Task HandleAsync(MyEvent message, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);

        _ = await Assert.That(diagnostics).IsEmpty();
        _ = await Assert.That(generatedSources).Count().IsEqualTo(1);

        var generatedCode = generatedSources[0].SourceText.ToString();
        _ = await Assert.That(generatedCode).Contains("EventHandlerA");
        _ = await Assert.That(generatedCode).Contains("EventHandlerB");
    }

    [Test]
    public async Task WhenNoPulseHandlerClassesThenNoOutputGenerated()
    {
        var source = """
            using NetEvolve.Pulse.Extensibility;
            using System.Threading;
            using System.Threading.Tasks;

            public record MyCommand(string Name) : ICommand<string>;

            public class MyCommandHandler : ICommandHandler<MyCommand, string>
            {
                public Task<string> HandleAsync(MyCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult(command.Name);
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);

        _ = await Assert.That(diagnostics).IsEmpty();
        _ = await Assert.That(generatedSources).IsEmpty();
    }

    [Test]
    public async Task WhenSingletonLifetimeSpecifiedThenTryAddSingletonIsGenerated()
    {
        var source = """
            using NetEvolve.Pulse.SourceGeneration;
            using NetEvolve.Pulse.Extensibility;
            using System.Threading;
            using System.Threading.Tasks;

            public record MyQuery(string Id) : IQuery<string>;

            [PulseHandler(Lifetime = PulseServiceLifetime.Singleton)]
            public class MyQueryHandler : IQueryHandler<MyQuery, string>
            {
                public Task<string> HandleAsync(MyQuery request, CancellationToken cancellationToken = default)
                    => Task.FromResult(request.Id);
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);

        _ = await Assert.That(diagnostics).IsEmpty();
        _ = await Assert.That(generatedSources).Count().IsEqualTo(1);

        var generatedCode = generatedSources[0].SourceText.ToString();
        _ = await Assert.That(generatedCode).Contains("TryAddSingleton");
        _ = await Assert.That(generatedCode).Contains("MyQueryHandler");
    }

    [Test]
    public async Task WhenTransientLifetimeSpecifiedThenTryAddTransientIsGenerated()
    {
        var source = """
            using NetEvolve.Pulse.SourceGeneration;
            using NetEvolve.Pulse.Extensibility;
            using System.Threading;
            using System.Threading.Tasks;
            using System;

            public record MyEvent : IEvent
            {
                public string Id { get; init; } = Guid.NewGuid().ToString();
                public string? CorrelationId { get; set; }
                public DateTimeOffset? PublishedAt { get; set; }
            }

            [PulseHandler(Lifetime = PulseServiceLifetime.Transient)]
            public class MyEventHandler : IEventHandler<MyEvent>
            {
                public Task HandleAsync(MyEvent message, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);

        _ = await Assert.That(diagnostics).IsEmpty();
        _ = await Assert.That(generatedSources).Count().IsEqualTo(1);

        var generatedCode = generatedSources[0].SourceText.ToString();
        _ = await Assert.That(generatedCode).Contains("TryAddTransient");
        _ = await Assert.That(generatedCode).Contains("MyEventHandler");
    }

    [Test]
    public async Task WhenScopedLifetimeExplicitlySpecifiedThenTryAddScopedIsGenerated()
    {
        var source = """
            using NetEvolve.Pulse.SourceGeneration;
            using NetEvolve.Pulse.Extensibility;
            using System.Threading;
            using System.Threading.Tasks;

            public record MyCommand(string Name) : ICommand<string>;

            [PulseHandler(Lifetime = PulseServiceLifetime.Scoped)]
            public class MyCommandHandler : ICommandHandler<MyCommand, string>
            {
                public Task<string> HandleAsync(MyCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult(command.Name);
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);

        _ = await Assert.That(diagnostics).IsEmpty();
        _ = await Assert.That(generatedSources).Count().IsEqualTo(1);

        var generatedCode = generatedSources[0].SourceText.ToString();
        _ = await Assert.That(generatedCode).Contains("TryAddScoped");
        _ = await Assert.That(generatedCode).Contains("MyCommandHandler");
    }

    [Test]
    public async Task WhenRootNamespaceProvidedThenGeneratedCodeUsesIt()
    {
        var source = """
            using NetEvolve.Pulse.SourceGeneration;
            using NetEvolve.Pulse.Extensibility;
            using System.Threading;
            using System.Threading.Tasks;

            public record MyCommand(string Name) : ICommand<string>;

            [PulseHandler]
            public class MyCommandHandler : ICommandHandler<MyCommand, string>
            {
                public Task<string> HandleAsync(MyCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult(command.Name);
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source, rootNamespace: "MyApp.Custom");

        _ = await Assert.That(diagnostics).IsEmpty();
        _ = await Assert.That(generatedSources).Count().IsEqualTo(1);

        var generatedCode = generatedSources[0].SourceText.ToString();
        _ = await Assert.That(generatedCode).Contains("namespace MyApp.Custom");
    }

    [Test]
    public async Task WhenNoRootNamespaceProvidedThenDefaultNamespaceIsUsed()
    {
        var source = """
            using NetEvolve.Pulse.SourceGeneration;
            using NetEvolve.Pulse.Extensibility;
            using System.Threading;
            using System.Threading.Tasks;

            public record MyCommand(string Name) : ICommand<string>;

            [PulseHandler]
            public class MyCommandHandler : ICommandHandler<MyCommand, string>
            {
                public Task<string> HandleAsync(MyCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult(command.Name);
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source, rootNamespace: null);

        _ = await Assert.That(diagnostics).IsEmpty();
        _ = await Assert.That(generatedSources).Count().IsEqualTo(1);

        var generatedCode = generatedSources[0].SourceText.ToString();
        _ = await Assert.That(generatedCode).Contains("namespace NetEvolve.Pulse.Generated");
    }

    [Test]
    public async Task WhenGeneratedCodeEmittedThenItContainsAutoGeneratedComment()
    {
        var source = """
            using NetEvolve.Pulse.SourceGeneration;
            using NetEvolve.Pulse.Extensibility;
            using System.Threading;
            using System.Threading.Tasks;

            public record MyCommand(string Name) : ICommand<string>;

            [PulseHandler]
            public class MyCommandHandler : ICommandHandler<MyCommand, string>
            {
                public Task<string> HandleAsync(MyCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult(command.Name);
            }
            """;

        var (_, generatedSources) = RunGenerator(source);

        var generatedCode = generatedSources[0].SourceText.ToString();
        _ = await Assert.That(generatedCode).Contains("// <auto-generated />");
        _ = await Assert.That(generatedCode).Contains("GeneratedCode");
        _ = await Assert.That(generatedCode).Contains("PulseHandlerRegistrationExtensions");
    }

    [Test]
    public async Task WhenGeneratedCodeEmittedThenItUsesFullyQualifiedServiceCollectionDescriptorExtensions()
    {
        var source = """
            using NetEvolve.Pulse.SourceGeneration;
            using NetEvolve.Pulse.Extensibility;
            using System.Threading;
            using System.Threading.Tasks;

            public record MyCommand(string Name) : ICommand<string>;

            [PulseHandler]
            public class MyCommandHandler : ICommandHandler<MyCommand, string>
            {
                public Task<string> HandleAsync(MyCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult(command.Name);
            }
            """;

        var (_, generatedSources) = RunGenerator(source);

        var generatedCode = generatedSources[0].SourceText.ToString();
        _ = await Assert
            .That(generatedCode)
            .Contains(
                "global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddScoped"
            );
    }

    [Test]
    public async Task WhenPrioritizedEventHandlerAnnotatedThenRegisteredAsIEventHandler()
    {
        var source = """
            using NetEvolve.Pulse.SourceGeneration;
            using NetEvolve.Pulse.Extensibility;
            using System.Threading;
            using System.Threading.Tasks;
            using System;

            public record MyEvent : IEvent
            {
                public string Id { get; init; } = Guid.NewGuid().ToString();
                public string? CorrelationId { get; set; }
                public DateTimeOffset? PublishedAt { get; set; }
            }

            [PulseHandler]
            public class MyPrioritizedHandler : IPrioritizedEventHandler<MyEvent>
            {
                public int Priority => 0;

                public Task HandleAsync(MyEvent message, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);

        _ = await Assert.That(diagnostics).IsEmpty();
        _ = await Assert.That(generatedSources).Count().IsEqualTo(1);

        var generatedCode = generatedSources[0].SourceText.ToString();
        _ = await Assert.That(generatedCode).Contains("IEventHandler");
        _ = await Assert.That(generatedCode).Contains("MyPrioritizedHandler");
    }

    [Test]
    public async Task WhenCommandHandlerVoidAnnotatedThenRegistrationIsGenerated()
    {
        var source = """
            using NetEvolve.Pulse.SourceGeneration;
            using NetEvolve.Pulse.Extensibility;
            using System.Threading;
            using System.Threading.Tasks;

            public record MyVoidCommand(string Name) : ICommand;

            [PulseHandler]
            public class MyVoidCommandHandler : ICommandHandler<MyVoidCommand, Void>
            {
                public Task<Void> HandleAsync(MyVoidCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult(Void.Completed);
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);

        _ = await Assert.That(diagnostics).IsEmpty();
        _ = await Assert.That(generatedSources).Count().IsEqualTo(1);

        var generatedCode = generatedSources[0].SourceText.ToString();
        _ = await Assert.That(generatedCode).Contains("ICommandHandler");
        _ = await Assert.That(generatedCode).Contains("MyVoidCommandHandler");
    }

    [Test]
    public async Task WhenMultipleHandlerClassesThenAllRegistered()
    {
        var source = """
            using NetEvolve.Pulse.SourceGeneration;
            using NetEvolve.Pulse.Extensibility;
            using System.Threading;
            using System.Threading.Tasks;

            public record CommandA(string Name) : ICommand<string>;
            public record CommandB(int Value) : ICommand<int>;

            [PulseHandler]
            public class HandlerA : ICommandHandler<CommandA, string>
            {
                public Task<string> HandleAsync(CommandA command, CancellationToken cancellationToken = default)
                    => Task.FromResult(command.Name);
            }

            [PulseHandler]
            public class HandlerB : ICommandHandler<CommandB, int>
            {
                public Task<int> HandleAsync(CommandB command, CancellationToken cancellationToken = default)
                    => Task.FromResult(command.Value);
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);

        _ = await Assert.That(diagnostics).IsEmpty();
        _ = await Assert.That(generatedSources).Count().IsEqualTo(1);

        var generatedCode = generatedSources[0].SourceText.ToString();
        _ = await Assert.That(generatedCode).Contains("HandlerA");
        _ = await Assert.That(generatedCode).Contains("HandlerB");
    }

    [Test]
    public async Task WhenHandlersHaveDifferentLifetimesThenEachUsesItsOwn()
    {
        var source = """
            using NetEvolve.Pulse.SourceGeneration;
            using NetEvolve.Pulse.Extensibility;
            using System.Threading;
            using System.Threading.Tasks;

            public record CommandA(string Name) : ICommand<string>;
            public record QueryA(string Id) : IQuery<string>;

            [PulseHandler(Lifetime = PulseServiceLifetime.Singleton)]
            public class SingletonHandler : ICommandHandler<CommandA, string>
            {
                public Task<string> HandleAsync(CommandA command, CancellationToken cancellationToken = default)
                    => Task.FromResult(command.Name);
            }

            [PulseHandler(Lifetime = PulseServiceLifetime.Transient)]
            public class TransientHandler : IQueryHandler<QueryA, string>
            {
                public Task<string> HandleAsync(QueryA query, CancellationToken cancellationToken = default)
                    => Task.FromResult(query.Id);
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);

        _ = await Assert.That(diagnostics).IsEmpty();
        _ = await Assert.That(generatedSources).Count().IsEqualTo(1);

        var generatedCode = generatedSources[0].SourceText.ToString();
        _ = await Assert.That(generatedCode).Contains("TryAddSingleton");
        _ = await Assert.That(generatedCode).Contains("TryAddTransient");
    }

    [Test]
    public async Task WhenNoHandlerInterfaceThenNoSourceGenerated()
    {
        var source = """
            using NetEvolve.Pulse.SourceGeneration;

            [PulseHandler]
            public class NotAHandler
            {
            }
            """;

        var (_, generatedSources) = RunGenerator(source);

        _ = await Assert.That(generatedSources).IsEmpty();
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, ImmutableArray<GeneratedSourceResult> Sources) RunGenerator(
        string source,
        string? rootNamespace = "TestAssembly"
    )
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = GetMetadataReferences();

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var generator = new PulseHandlerGenerator();
        var optionsProvider = new TestAnalyzerConfigOptionsProvider(rootNamespace);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            optionsProvider: optionsProvider
        );

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics);

        var runResult = driver.GetRunResult();
        var generatorResult = runResult.Results.Single();

        // Only return generator-specific diagnostics (PULSE*), not compilation diagnostics.
        var pulseDiagnostics = generatorDiagnostics
            .Where(d => d.Id.StartsWith("PULSE", System.StringComparison.Ordinal))
            .ToImmutableArray();

        return (pulseDiagnostics, generatorResult.GeneratedSources);
    }

    private static MetadataReference[] GetMetadataReferences()
    {
        // Core runtime references
        var trustedAssemblies = System.AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        var runtimeReferences = trustedAssemblies!
            .Split(System.IO.Path.PathSeparator)
            .Where(p =>
            {
                var fileName = System.IO.Path.GetFileName(p);
                return fileName.StartsWith("System.", System.StringComparison.Ordinal)
                    || fileName == "mscorlib.dll"
                    || fileName == "netstandard.dll";
            })
            .Select(p => MetadataReference.CreateFromFile(p))
            .Cast<MetadataReference>()
            .ToList();

        // Add the Pulse assemblies
        runtimeReferences.Add(
            MetadataReference.CreateFromFile(typeof(NetEvolve.Pulse.Extensibility.ICommand<>).Assembly.Location)
        );
        runtimeReferences.Add(
            MetadataReference.CreateFromFile(
                typeof(NetEvolve.Pulse.SourceGeneration.PulseHandlerAttribute).Assembly.Location
            )
        );

        return runtimeReferences.ToArray();
    }
}
