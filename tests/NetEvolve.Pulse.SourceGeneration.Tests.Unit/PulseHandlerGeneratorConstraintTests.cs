namespace NetEvolve.Pulse.SourceGeneration.Tests.Unit;

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.SourceGeneration.Generators;
using TUnit.Core;

/// <summary>
/// Verifies that <c>[PulseHandler&lt;T&gt;]</c> on a generic handler whose type parameter constraints are not
/// satisfied by the message type reports PULSE006 instead of emitting a registration that does not compile.
/// </summary>
[TestGroup("SourceGeneration")]
[TestGroup("SourceGeneration.PulseHandler")]
public class PulseHandlerGeneratorConstraintTests
{
    private const string Preamble = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using NetEvolve.Pulse.Extensibility;
        using NetEvolve.Pulse.Extensibility.Attributes;

        public interface IMarker { }

        """;

    [Test]
    [Arguments(
        "FixedResultTypeMismatch",
        """
            public sealed record C1 : ICommand<string>;

            [PulseHandler<C1>]
            public sealed class G1<TCmd> : ICommandHandler<TCmd, int>
                where TCmd : ICommand<int>
            {
                public Task<int> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(0);
            }
            """
    )]
    [Arguments(
        "ExtraInterfaceConstraint",
        """
            public sealed record C1 : ICommand<string>;

            [PulseHandler<C1>]
            public sealed class G1<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : ICommand<TResult>, IMarker
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult)!);
            }
            """
    )]
    [Arguments(
        "ClassConstraintOnValueTypeMessage",
        """
            public record struct C1 : ICommand<string>;

            [PulseHandler<C1>]
            public sealed class G1<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : class, ICommand<TResult>
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult)!);
            }
            """
    )]
    [Arguments(
        "StructConstraintOnReferenceTypeMessage",
        """
            public sealed record C1 : ICommand<string>;

            [PulseHandler<C1>]
            public sealed class G1<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : struct, ICommand<TResult>
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult)!);
            }
            """
    )]
    [Arguments(
        "NewConstraintWithoutParameterlessConstructor",
        """
            public sealed record C1(string Name) : ICommand<string>;

            [PulseHandler<C1>]
            public sealed class G1<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : ICommand<TResult>, new()
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult)!);
            }
            """
    )]
    [Arguments(
        "NewConstraintOnAbstractMessage",
        """
            public abstract record C1 : ICommand<string>;

            [PulseHandler<C1>]
            public sealed class G1<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : ICommand<TResult>, new()
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult)!);
            }
            """
    )]
    [Arguments(
        "BaseTypeConstraint",
        """
            public abstract record CommandBase : ICommand<string>;
            public sealed record C1 : ICommand<string>;

            [PulseHandler<C1>]
            public sealed class G1<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : CommandBase, ICommand<TResult>
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult)!);
            }
            """
    )]
    [Arguments(
        "NestedGenericConstraint",
        """
            public sealed record C1 : ICommand<string>;

            [PulseHandler<C1>]
            public sealed class G1<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : ICommand<TResult>, IComparable<TCmd>
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult)!);
            }
            """
    )]
    [Arguments(
        "StructConstraintOnResultTypeParameter",
        """
            public sealed record Q1 : IQuery<string>;

            [PulseHandler<Q1>]
            public sealed class G1<TQuery, TResult> : IQueryHandler<TQuery, TResult>
                where TQuery : IQuery<TResult>
                where TResult : struct
            {
                public Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult));
            }
            """
    )]
    [Arguments(
        "UnmanagedConstraintOnResultTypeParameter",
        """
            public sealed record Q1 : IQuery<string>;

            [PulseHandler<Q1>]
            public sealed class G1<TQuery, TResult> : IQueryHandler<TQuery, TResult>
                where TQuery : IQuery<TResult>
                where TResult : unmanaged
            {
                public Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult));
            }
            """
    )]
    [Arguments(
        "NotNullConstraintOnNullableResultTypeParameter",
        """
            public sealed record C1 : ICommand<int?>;

            [PulseHandler<C1>]
            public sealed class G1<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : ICommand<TResult>
                where TResult : notnull
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult)!);
            }
            """
    )]
    [Arguments(
        "ClassConstraintOnStreamQueryResult",
        """
            public sealed record S1 : IStreamQuery<int>;

            [PulseHandler<S1>]
            public sealed class G1<TQuery, TResult> : IStreamQueryHandler<TQuery, TResult>
                where TQuery : IStreamQuery<TResult>
                where TResult : class
            {
                public IAsyncEnumerable<TResult> HandleAsync(TQuery request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            }
            """
    )]
    [Arguments(
        "ExtraInterfaceConstraintOnEventHandler",
        """
            public sealed record E1 : IEvent
            {
                public string Id { get; init; } = Guid.NewGuid().ToString();
                public string? CausationId { get; set; }
                public string? CorrelationId { get; set; }
                public DateTimeOffset? PublishedAt { get; set; }
            }

            [PulseHandler<E1>]
            public sealed class G1<TEvent> : IEventHandler<TEvent>
                where TEvent : IEvent, IMarker
            {
                public Task HandleAsync(TEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
            }
            """
    )]
    public async Task WhenGenericHandlerConstraintNotSatisfiedThenPulse006ReportedAndNoGeneratedCodeErrors(
        string scenario,
        string declarations
    )
    {
        var (pulseDiagnostics, generatedErrors) = RunGenerator(declarations);

        using (Assert.Multiple())
        {
            _ = await Assert.That(pulseDiagnostics.Select(d => d.Id)).IsEquivalentTo(["PULSE006"]).Because(scenario);
            _ = await Assert.That(generatedErrors).IsEmpty().Because(scenario);
        }
    }

    [Test]
    [Arguments(
        "NestedGenericConstraintSatisfied",
        """
            public sealed record C1 : ICommand<string>;

            [PulseHandler<C1>]
            public sealed class G1<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : class, ICommand<TResult>, IEquatable<TCmd>, new()
                where TResult : notnull
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult)!);
            }
            """
    )]
    [Arguments(
        "BaseTypeAndValueTypeConstraintsSatisfied",
        """
            public abstract record CommandBase : ICommand<int>;
            public sealed record C1 : CommandBase, IMarker;

            [PulseHandler<C1>]
            public sealed class G1<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : CommandBase, ICommand<TResult>, IMarker
                where TResult : unmanaged
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult));
            }
            """
    )]
    [Arguments(
        "EventHandlerConstraintsSatisfied",
        """
            public record struct E1 : IEvent, IMarker
            {
                public E1() { }
                public string Id { get; init; } = Guid.NewGuid().ToString();
                public string? CausationId { get; set; }
                public string? CorrelationId { get; set; }
                public DateTimeOffset? PublishedAt { get; set; }
            }

            [PulseHandler<E1>]
            public sealed class G1<TEvent> : IEventHandler<TEvent>
                where TEvent : struct, IEvent, IMarker
            {
                public Task HandleAsync(TEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
            }
            """
    )]
    public async Task WhenGenericHandlerConstraintsSatisfiedThenRegistrationGeneratedWithoutErrors(
        string scenario,
        string declarations
    )
    {
        var (pulseDiagnostics, generatedErrors) = RunGenerator(declarations);

        using (Assert.Multiple())
        {
            _ = await Assert.That(pulseDiagnostics).IsEmpty().Because(scenario);
            _ = await Assert.That(generatedErrors).IsEmpty().Because(scenario);
        }
    }

    private static (Diagnostic[] PulseDiagnostics, Diagnostic[] GeneratedErrors) RunGenerator(string declarations)
    {
        var references = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)!
            .Split(Path.PathSeparator)
            .Where(path =>
            {
                var fileName = Path.GetFileName(path);
                return fileName.StartsWith("System.", StringComparison.Ordinal)
                    || fileName.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal)
                    || string.Equals(fileName, "NetEvolve.Pulse.Extensibility.dll", StringComparison.Ordinal)
                    || string.Equals(fileName, "netstandard.dll", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fileName, "mscorlib.dll", StringComparison.OrdinalIgnoreCase);
            })
            .Select(path => MetadataReference.CreateFromFile(path));

        var inputTree = CSharpSyntaxTree.ParseText(
            Preamble + declarations,
            new CSharpParseOptions(LanguageVersion.Latest)
        );
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [inputTree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

        _ = CSharpGeneratorDriver
            .Create(
                generators: [new PulseHandlerGenerator().AsSourceGenerator()],
                optionsProvider: new TestAnalyzerConfigOptionsProvider("TestAssembly")
            )
            .RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);

        var pulseDiagnostics = generatorDiagnostics
            .Where(d => d.Id.StartsWith("PULSE", StringComparison.Ordinal))
            .ToArray();
        var generatedErrors = outputCompilation
            .GetDiagnostics()
            .Where(d =>
                d.Severity == DiagnosticSeverity.Error
                && d.Location.SourceTree is not null
                && d.Location.SourceTree != inputTree
            )
            .ToArray();

        return (pulseDiagnostics, generatedErrors);
    }
}
