namespace NetEvolve.Pulse.SourceGeneration.Tests.Unit;

using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.SourceGeneration.Generators;
using TUnit.Core;

[TestGroup("SourceGeneration")]
[TestGroup("SourceGeneration.PulseHandler")]
public class PulseHandlerGeneratorTests
{
    [Test]
    public async Task WhenCommandHandlerAnnotatedThenRegistrationIsGenerated()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
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
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenQueryHandlerAnnotatedThenRegistrationIsGenerated()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
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
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenEventHandlerAnnotatedThenRegistrationIsGenerated()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
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
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenMultipleInterfacesImplementedThenAllRegistered()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
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
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenNoHandlerInterfaceImplementedThenPulse001Reported()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;

            [PulseHandler]
            public class NotAHandler
            {
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenNoHandlerInterfaceImplementedThenPulse001MessageContainsTypeName()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;

            [PulseHandler]
            public class NotAHandler
            {
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenDuplicateCommandHandlersThenPulse002Reported()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
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
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenDuplicateCommandHandlersThenPulse002MessageContainsBothHandlerNames()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
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
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenDuplicateQueryHandlersThenPulse002Reported()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
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

        var (diagnostics, generatedSources) = RunGenerator(source);
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenMultipleEventHandlersForSameEventThenNoPulse002()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
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
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenNoPulseHandlerClassesThenNoOutputGenerated()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
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
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenSingletonLifetimeSpecifiedThenTryAddSingletonIsGenerated()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
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
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenTransientLifetimeSpecifiedThenTryAddTransientIsGenerated()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
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
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenScopedLifetimeExplicitlySpecifiedThenTryAddScopedIsGenerated()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
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
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenRootNamespaceProvidedThenGeneratedCodeUsesIt()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
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
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenNoRootNamespaceProvidedThenDefaultNamespaceIsUsed()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
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
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenDottedAssemblyNameThenMethodNameRemovesDots()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
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
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenGeneratedCodeEmittedThenItContainsAutoGeneratedComment()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
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
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenGeneratedCodeEmittedThenItUsesFullyQualifiedServiceCollectionDescriptorExtensions()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
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
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenPrioritizedEventHandlerAnnotatedThenRegisteredAsIEventHandler()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
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
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenCommandHandlerVoidAnnotatedThenRegistrationIsGenerated()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
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
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenMultipleHandlerClassesThenAllRegistered()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
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
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenHandlersHaveDifferentLifetimesThenEachUsesItsOwn()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
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
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenNoHandlerInterfaceThenNoSourceGenerated()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;

            [PulseHandler]
            public class NotAHandler
            {
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenStreamQueryHandlerAnnotatedThenRegistrationIsGenerated()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
            using System.Collections.Generic;
            using System.Threading;

            public record MyStreamQuery(string Id) : IStreamQuery<string>;

            [PulseHandler]
            public class MyStreamQueryHandler : IStreamQueryHandler<MyStreamQuery, string>
            {
                public async IAsyncEnumerable<string> HandleAsync(MyStreamQuery request, CancellationToken cancellationToken = default)
                {
                    yield return request.Id;
                }
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenDuplicateStreamQueryHandlersThenPulse002Reported()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
            using System.Collections.Generic;
            using System.Threading;

            public record MyStreamQuery(string Id) : IStreamQuery<string>;

            [PulseHandler]
            public class StreamQueryHandlerA : IStreamQueryHandler<MyStreamQuery, string>
            {
                public async IAsyncEnumerable<string> HandleAsync(MyStreamQuery request, CancellationToken cancellationToken = default)
                {
                    yield return "A";
                }
            }

            [PulseHandler]
            public class StreamQueryHandlerB : IStreamQueryHandler<MyStreamQuery, string>
            {
                public async IAsyncEnumerable<string> HandleAsync(MyStreamQuery request, CancellationToken cancellationToken = default)
                {
                    yield return "B";
                }
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenQueryHandlerNotAnnotatedThenPulse003Reported()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            public record MyQuery(string Id) : IQuery<string>;

            public class MyQueryHandler : IQueryHandler<MyQuery, string>
            {
                public Task<string> HandleAsync(MyQuery request, CancellationToken cancellationToken = default)
                    => Task.FromResult(request.Id);
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenEventHandlerNotAnnotatedThenPulse003Reported()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
            using System.Threading;
            using System.Threading.Tasks;
            using System;

            public record MyEvent : IEvent
            {
                public string Id { get; init; } = Guid.NewGuid().ToString();
                public string? CorrelationId { get; set; }
                public DateTimeOffset? PublishedAt { get; set; }
            }

            public class MyEventHandler : IEventHandler<MyEvent>
            {
                public Task HandleAsync(MyEvent message, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenStreamQueryHandlerNotAnnotatedThenPulse003Reported()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
            using System.Collections.Generic;
            using System.Threading;

            public record MyStreamQuery(string Id) : IStreamQuery<string>;

            public class MyStreamQueryHandler : IStreamQueryHandler<MyStreamQuery, string>
            {
                public async IAsyncEnumerable<string> HandleAsync(MyStreamQuery request, CancellationToken cancellationToken = default)
                {
                    yield return request.Id;
                }
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenMultipleUnannotatedHandlersThenPulse003ReportedForEach()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            public record CommandA(string Name) : ICommand<string>;
            public record QueryA(string Id) : IQuery<string>;

            public class HandlerA : ICommandHandler<CommandA, string>
            {
                public Task<string> HandleAsync(CommandA command, CancellationToken cancellationToken = default)
                    => Task.FromResult(command.Name);
            }

            public class HandlerB : IQueryHandler<QueryA, string>
            {
                public Task<string> HandleAsync(QueryA query, CancellationToken cancellationToken = default)
                    => Task.FromResult(query.Id);
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenEmptyAssemblyNameThenFallbackMethodNameIsGenerated()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
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

        var (diagnostics, generatedSources) = RunGenerator(source, assemblyName: "");
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenRecordCommandHandlerAnnotatedThenRegistrationIsGenerated()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            public record MyCommand(string Name) : ICommand<string>;

            [PulseHandler]
            public record MyCommandHandler : ICommandHandler<MyCommand, string>
            {
                public Task<string> HandleAsync(MyCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult(command.Name);
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenRecordHandlerNotAnnotatedThenPulse003Reported()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            public record MyCommand(string Name) : ICommand<string>;

            public record MyCommandHandler : ICommandHandler<MyCommand, string>
            {
                public Task<string> HandleAsync(MyCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult(command.Name);
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenOpenGenericHandlerAnnotatedThenPulse004Reported()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            [PulseHandler]
            public class GenericHandler<TCommand, TResult> : ICommandHandler<TCommand, TResult>
                where TCommand : ICommand<TResult>
            {
                public Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult(default(TResult)!);
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenOpenGenericHandlerNotAnnotatedThenNoPulse003()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            public class GenericHandler<TCommand, TResult> : ICommandHandler<TCommand, TResult>
                where TCommand : ICommand<TResult>
            {
                public Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult(default(TResult)!);
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenOpenGenericCommandHandlerWithExplicitMessageTypeThenRegistrationIsGenerated()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            public record MyCommand(string Name) : ICommand<string>;

            [PulseHandler<MyCommand>]
            public class GenericCommandHandler<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : ICommand<TResult>
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default)
                    => Task.FromResult(default(TResult)!);
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenOpenGenericHandlerWithMultipleExplicitMessageTypesThenAllRegistered()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            public record CommandA(string Name) : ICommand<string>;
            public record CommandB(int Value) : ICommand<int>;

            [PulseHandler<CommandA>]
            [PulseHandler<CommandB>]
            public class GenericCommandHandler<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : ICommand<TResult>
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default)
                    => Task.FromResult(default(TResult)!);
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenOpenGenericEventHandlerWithExplicitMessageTypeThenRegistrationIsGenerated()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
            using System.Threading;
            using System.Threading.Tasks;
            using System;

            public record MyEvent : IEvent
            {
                public string Id { get; init; } = Guid.NewGuid().ToString();
                public string? CorrelationId { get; set; }
                public DateTimeOffset? PublishedAt { get; set; }
            }

            [PulseHandler<MyEvent>]
            public class GenericEventHandler<TEvent> : IEventHandler<TEvent>
                where TEvent : IEvent
            {
                public Task HandleAsync(TEvent message, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenOpenGenericHandlerWithExplicitMessageTypeAndSingletonLifetimeThenSingletonIsGenerated()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            public record MyQuery(string Id) : IQuery<string>;

            [PulseHandler<MyQuery>(Lifetime = PulseServiceLifetime.Singleton)]
            public class GenericQueryHandler<TQuery, TResult> : IQueryHandler<TQuery, TResult>
                where TQuery : IQuery<TResult>
            {
                public Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken = default)
                    => Task.FromResult(default(TResult)!);
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenExplicitMessageTypeNotPulseMessageThenPulse005Reported()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            [PulseHandler<string>]
            public class GenericCommandHandler<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : ICommand<TResult>
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default)
                    => Task.FromResult(default(TResult)!);
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenExplicitMessageTypeIncompatibleWithHandlerInterfaceThenPulse006Reported()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
            using System.Threading;
            using System.Threading.Tasks;
            using System;

            public record MyEvent : IEvent
            {
                public string Id { get; init; } = Guid.NewGuid().ToString();
                public string? CorrelationId { get; set; }
                public DateTimeOffset? PublishedAt { get; set; }
            }

            [PulseHandler<MyEvent>]
            public class GenericCommandHandler<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : ICommand<TResult>
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default)
                    => Task.FromResult(default(TResult)!);
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenConcreteHandlerAnnotatedWithGenericAttributeThenRegistrationIsGenerated()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            public record MyCommand(string Name) : ICommand<string>;

            [PulseHandler<MyCommand>]
            public class MyCommandHandler : ICommandHandler<MyCommand, string>
            {
                public Task<string> HandleAsync(MyCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult(command.Name);
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenSingleHandlerAndMultiInterfaceHandlerAndMultipleOpenGenericHandlersThenAllRegistered()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
            using System.Threading;
            using System.Threading.Tasks;
            using System;

            // --- single concrete handler ---
            public record MyCommand(string Name) : ICommand<string>;

            [PulseHandler]
            public class MyCommandHandler : ICommandHandler<MyCommand, string>
            {
                public Task<string> HandleAsync(MyCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult(command.Name);
            }

            // --- multi-interface concrete handler ---
            public record AnotherCommand(int Value) : ICommand<int>;

            public record MyEvent : IEvent
            {
                public string Id { get; init; } = Guid.NewGuid().ToString();
                public string? CorrelationId { get; set; }
                public DateTimeOffset? PublishedAt { get; set; }
            }

            [PulseHandler]
            public class MultiHandler : ICommandHandler<AnotherCommand, int>, IEventHandler<MyEvent>
            {
                public Task<int> HandleAsync(AnotherCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult(command.Value);

                public Task HandleAsync(MyEvent message, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }

            // --- open-generic handler with multiple explicit message types ---
            public record QueryA(string Id) : IQuery<string>;
            public record QueryB(int Id) : IQuery<int>;

            [PulseHandler<QueryA>]
            [PulseHandler<QueryB>]
            public class GenericQueryHandler<TQuery, TResult> : IQueryHandler<TQuery, TResult>
                where TQuery : IQuery<TResult>
            {
                public Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken = default)
                    => Task.FromResult(default(TResult)!);
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenPureGenericHandlerAnnotatedThenOpenGenericRegistrationIsGenerated()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            [PulseGenericHandler]
            public class GenericCommandHandler<TCommand, TResult> : ICommandHandler<TCommand, TResult>
                where TCommand : ICommand<TResult>
            {
                public Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult(default(TResult)!);
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenPureGenericEventHandlerAnnotatedThenOpenGenericRegistrationIsGenerated()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
            using System.Threading;
            using System.Threading.Tasks;
            using System;

            [PulseGenericHandler]
            public class GenericEventHandler<TEvent> : IEventHandler<TEvent>
                where TEvent : IEvent
            {
                public Task HandleAsync(TEvent message, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenPureGenericHandlerWithSingletonLifetimeThenSingletonOpenGenericRegistrationIsGenerated()
    {
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            [PulseGenericHandler(Lifetime = PulseServiceLifetime.Singleton)]
            public class GenericCommandHandler<TCommand, TResult> : ICommandHandler<TCommand, TResult>
                where TCommand : ICommand<TResult>
            {
                public Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult(default(TResult)!);
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    [Test]
    public async Task WhenPureGenericHandlerNotAnnotatedThenNoPulse003()
    {
        // Open generic types without [PulseGenericHandler] must NOT trigger PULSE003
        // (same behaviour as with [PulseHandler] – PULSE003 is suppressed for open generics).
        const string source = """
            using NetEvolve.Pulse.Extensibility;
            using NetEvolve.Pulse.Extensibility.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            public class GenericCommandHandler<TCommand, TResult> : ICommandHandler<TCommand, TResult>
                where TCommand : ICommand<TResult>
            {
                public Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult(default(TResult)!);
            }
            """;

        var (diagnostics, generatedSources) = RunGenerator(source);
        await VerifySources(diagnostics, generatedSources).ConfigureAwait(false);
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, ImmutableArray<string> Sources) RunGenerator(
        string source,
        string? rootNamespace = "TestAssembly",
        string assemblyName = "TestAssembly"
    )
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = GetMetadataReferences();

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var generator = new PulseHandlerGenerator();
        var optionsProvider = new TestAnalyzerConfigOptionsProvider(rootNamespace);

        var driver = CSharpGeneratorDriver
            .Create(generators: [generator.AsSourceGenerator()], optionsProvider: optionsProvider)
            .RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics);

        var runResult = driver.GetRunResult();
        var generatorResult = runResult.Results.Single();

        // Only return generator-specific diagnostics (PULSE*), not compilation diagnostics.
        var pulseDiagnostics = generatorDiagnostics
            .Where(d => d.Id.StartsWith("PULSE", StringComparison.Ordinal))
            .ToImmutableArray();

        return (
            pulseDiagnostics,
            generatorResult.GeneratedSources.Select(x => x.SourceText.ToString()).ToImmutableArray()
        );
    }

    private static MetadataReference[] GetMetadataReferences()
    {
        // Core runtime references
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        var runtimeReferences = trustedAssemblies!
            .Split(Path.PathSeparator)
            .Where(p =>
            {
                var fileName = Path.GetFileName(p);
                return fileName.StartsWith("System.", StringComparison.Ordinal)
                    || string.Equals(fileName, "mscorlib.dll", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fileName, "netstandard.dll", StringComparison.OrdinalIgnoreCase);
            })
            .Select(p => MetadataReference.CreateFromFile(p))
            .Cast<MetadataReference>()
            .ToList();

        // Add the Pulse assemblies
        runtimeReferences.Add(MetadataReference.CreateFromFile(typeof(Extensibility.ICommand<>).Assembly.Location));
        runtimeReferences.Add(
            MetadataReference.CreateFromFile(typeof(Extensibility.Attributes.PulseHandlerAttribute).Assembly.Location)
        );

        return [.. runtimeReferences];
    }

    private static async Task VerifySources(ImmutableArray<Diagnostic> diagnostics, ImmutableArray<string> sources) =>
        await Verify(new { diagnostics, sources }).ConfigureAwait(false);
}
