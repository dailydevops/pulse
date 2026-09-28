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
/// Verifies that handlers which generated code cannot reference or DI cannot instantiate (inaccessible,
/// file-local, nested in a generic type, abstract, static or value types) report a diagnostic on the
/// declaration instead of emitting a registration that does not compile or fails at runtime.
/// </summary>
[TestGroup("SourceGeneration")]
[TestGroup("SourceGeneration.PulseHandler")]
public class PulseHandlerGeneratorAccessibilityTests
{
    private const string Preamble = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using NetEvolve.Pulse.Extensibility;
        using NetEvolve.Pulse.Extensibility.Attributes;

        public abstract record RequestBase
        {
            public string? CausationId { get; set; }
            public string? CorrelationId { get; set; }
        }

        public sealed record C1 : RequestBase, ICommand<string>;

        """;

    [Test]
    [Arguments(
        "PrivateNested",
        """
            public class Outer
            {
                [PulseHandler]
                private sealed class Blocked : ICommandHandler<C1, string>
                {
                    public Task<string> HandleAsync(C1 command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
                }
            }
            """
    )]
    [Arguments(
        "ProtectedNested",
        """
            public class Outer
            {
                [PulseHandler]
                protected sealed class Blocked : ICommandHandler<C1, string>
                {
                    public Task<string> HandleAsync(C1 command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
                }
            }
            """
    )]
    [Arguments(
        "PrivateProtectedNested",
        """
            public class Outer
            {
                [PulseHandler]
                private protected sealed class Blocked : ICommandHandler<C1, string>
                {
                    public Task<string> HandleAsync(C1 command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
                }
            }
            """
    )]
    [Arguments(
        "InternalNestedInPrivateContainer",
        """
            public class Outer
            {
                private class Middle
                {
                    [PulseHandler]
                    internal sealed class Blocked : ICommandHandler<C1, string>
                    {
                        public Task<string> HandleAsync(C1 command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
                    }
                }
            }
            """
    )]
    [Arguments(
        "FileLocal",
        """
            [PulseHandler]
            file sealed class Blocked : ICommandHandler<C1, string>
            {
                public Task<string> HandleAsync(C1 command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
            }
            """
    )]
    [Arguments(
        "NestedInFileLocalContainer",
        """
            file class Outer
            {
                [PulseHandler]
                public sealed class Blocked : ICommandHandler<C1, string>
                {
                    public Task<string> HandleAsync(C1 command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
                }
            }
            """
    )]
    [Arguments(
        "Abstract",
        """
            [PulseHandler]
            public abstract class Blocked : ICommandHandler<C1, string>
            {
                public abstract Task<string> HandleAsync(C1 command, CancellationToken cancellationToken = default);
            }
            """
    )]
    [Arguments(
        "AbstractRecord",
        """
            [PulseHandler]
            public abstract record Blocked : ICommandHandler<C1, string>
            {
                public abstract Task<string> HandleAsync(C1 command, CancellationToken cancellationToken = default);
            }
            """
    )]
    [Arguments(
        "ExplicitPrivateNested",
        """
            public class Outer
            {
                [PulseHandler<C1>]
                private sealed class Blocked : ICommandHandler<C1, string>
                {
                    public Task<string> HandleAsync(C1 command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
                }
            }
            """
    )]
    [Arguments(
        "ExplicitGenericFileLocal",
        """
            [PulseHandler<C1>]
            file sealed class Blocked<TCmd> : ICommandHandler<TCmd, string>
                where TCmd : ICommand<string>
            {
                public Task<string> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
            }
            """
    )]
    [Arguments(
        "OpenGenericAbstract",
        """
            [PulseGenericHandler]
            public abstract class Blocked<TCmd> : ICommandHandler<TCmd, string>
                where TCmd : ICommand<string>
            {
                public abstract Task<string> HandleAsync(TCmd command, CancellationToken cancellationToken = default);
            }
            """
    )]
    [Arguments(
        "OpenGenericPrivateNested",
        """
            public class Outer
            {
                [PulseGenericHandler]
                private sealed class Blocked<TCmd> : ICommandHandler<TCmd, string>
                    where TCmd : ICommand<string>
                {
                    public Task<string> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
                }
            }
            """
    )]
    public async Task WhenHandlerCannotBeRegisteredThenPulse007Reported(string scenario, string declarations)
    {
        var result = RunGenerator(declarations);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.InputErrors).IsEmpty().Because(scenario);
            _ = await Assert
                .That(result.PulseDiagnostics.Select(d => d.Id))
                .IsEquivalentTo(["PULSE007"])
                .Because(scenario);
            _ = await Assert.That(result.GeneratedErrors).IsEmpty().Because(scenario);
            _ = await Assert.That(result.GeneratedSource).DoesNotContain("Blocked").Because(scenario);
        }
    }

    [Test]
    [Arguments(
        "Static",
        """
            [PulseHandler]
            public static class Blocked : ICommandHandler<C1, string>
            {
            }
            """
    )]
    [Arguments(
        "RecordStruct",
        """
            [PulseHandler]
            public readonly record struct Blocked : ICommandHandler<C1, string>
            {
                public Task<string> HandleAsync(C1 command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
            }
            """
    )]
    public async Task WhenInvalidDeclarationAnnotatedThenPulse007ReportedWithoutGeneratedErrors(
        string scenario,
        string declarations
    )
    {
        // The declaration itself is already invalid (CS0714 for a static class implementing an interface,
        // CS0592 for the class-only attribute on a struct), but the generator must not add CS0718/CS0452
        // errors to generated code the user cannot edit.
        var result = RunGenerator(declarations);

        using (Assert.Multiple())
        {
            _ = await Assert
                .That(result.PulseDiagnostics.Select(d => d.Id))
                .IsEquivalentTo(["PULSE007"])
                .Because(scenario);
            _ = await Assert.That(result.GeneratedErrors).IsEmpty().Because(scenario);
            _ = await Assert.That(result.GeneratedSource).DoesNotContain("Blocked").Because(scenario);
        }
    }

    [Test]
    [Arguments(
        "PulseHandler",
        """
            public class Outer<T>
            {
                [PulseHandler]
                public sealed class Blocked : ICommandHandler<C1, string>
                {
                    public Task<string> HandleAsync(C1 command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
                }
            }
            """
    )]
    [Arguments(
        "PulseHandlerNestedTwice",
        """
            public class Outer<T>
            {
                public class Middle
                {
                    [PulseHandler]
                    public sealed class Blocked : ICommandHandler<C1, string>
                    {
                        public Task<string> HandleAsync(C1 command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
                    }
                }
            }
            """
    )]
    [Arguments(
        "ExplicitPulseHandler",
        """
            public class Outer<T>
            {
                [PulseHandler<C1>]
                public sealed class Blocked : ICommandHandler<C1, string>
                {
                    public Task<string> HandleAsync(C1 command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
                }
            }
            """
    )]
    [Arguments(
        "PulseGenericHandler",
        """
            public class Outer<T>
            {
                [PulseGenericHandler]
                public sealed class Blocked<TCmd> : ICommandHandler<TCmd, string>
                    where TCmd : ICommand<string>
                {
                    public Task<string> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
                }
            }
            """
    )]
    public async Task WhenHandlerNestedInGenericTypeThenPulse004Reported(string scenario, string declarations)
    {
        var result = RunGenerator(declarations);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.InputErrors).IsEmpty().Because(scenario);
            _ = await Assert
                .That(result.PulseDiagnostics.Select(d => d.Id))
                .IsEquivalentTo(["PULSE004"])
                .Because(scenario);
            _ = await Assert.That(result.GeneratedErrors).IsEmpty().Because(scenario);
            _ = await Assert.That(result.GeneratedSource).DoesNotContain("Blocked").Because(scenario);
        }
    }

    [Test]
    [Arguments(
        "InternalNested",
        """
            public class Outer
            {
                [PulseHandler]
                internal sealed class Allowed : ICommandHandler<C1, string>
                {
                    public Task<string> HandleAsync(C1 command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
                }
            }
            """,
        "global::Outer.Allowed"
    )]
    [Arguments(
        "ProtectedInternalNested",
        """
            public class Outer
            {
                [PulseHandler]
                protected internal sealed class Allowed : ICommandHandler<C1, string>
                {
                    public Task<string> HandleAsync(C1 command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
                }
            }
            """,
        "global::Outer.Allowed"
    )]
    [Arguments(
        "PublicNestedInInternalContainer",
        """
            internal class Outer
            {
                [PulseHandler]
                public sealed class Allowed : ICommandHandler<C1, string>
                {
                    public Task<string> HandleAsync(C1 command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
                }
            }
            """,
        "global::Outer.Allowed"
    )]
    [Arguments(
        "InternalTopLevel",
        """
            [PulseHandler]
            internal sealed class Allowed : ICommandHandler<C1, string>
            {
                public Task<string> HandleAsync(C1 command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
            }
            """,
        "global::Allowed"
    )]
    [Arguments(
        "SealedRecord",
        """
            [PulseHandler]
            public sealed record Allowed : ICommandHandler<C1, string>
            {
                public Task<string> HandleAsync(C1 command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
            }
            """,
        "global::Allowed"
    )]
    public async Task WhenHandlerIsAccessibleConcreteClassThenRegistered(
        string scenario,
        string declarations,
        string expectedHandler
    )
    {
        var result = RunGenerator(declarations);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.InputErrors).IsEmpty().Because(scenario);
            _ = await Assert.That(result.PulseDiagnostics).IsEmpty().Because(scenario);
            _ = await Assert.That(result.GeneratedErrors).IsEmpty().Because(scenario);
            _ = await Assert.That(result.GeneratedSource).Contains(expectedHandler).Because(scenario);
        }
    }

    [Test]
    [Arguments(
        "Abstract",
        """
            public abstract class Skipped : ICommandHandler<C1, string>
            {
                public abstract Task<string> HandleAsync(C1 command, CancellationToken cancellationToken = default);
            }
            """
    )]
    [Arguments(
        "PrivateNested",
        """
            public class Outer
            {
                private sealed class Skipped : ICommandHandler<C1, string>
                {
                    public Task<string> HandleAsync(C1 command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
                }
            }
            """
    )]
    [Arguments(
        "FileLocal",
        """
            file sealed class Skipped : ICommandHandler<C1, string>
            {
                public Task<string> HandleAsync(C1 command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
            }
            """
    )]
    [Arguments(
        "NestedInGenericType",
        """
            public class Outer<T>
            {
                public sealed class Skipped : ICommandHandler<C1, string>
                {
                    public Task<string> HandleAsync(C1 command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
                }
            }
            """
    )]
    [Arguments(
        "RecordStruct",
        """
            public readonly record struct Skipped : ICommandHandler<C1, string>
            {
                public Task<string> HandleAsync(C1 command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
            }
            """
    )]
    public async Task WhenUnannotatedHandlerCannotBeRegisteredThenNoPulse003(string scenario, string declarations)
    {
        var result = RunGenerator(declarations);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.InputErrors).IsEmpty().Because(scenario);
            _ = await Assert.That(result.PulseDiagnostics).IsEmpty().Because(scenario);
        }
    }

    private static GeneratorResult RunGenerator(string declarations)
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
            new CSharpParseOptions(LanguageVersion.Latest),
            path: "Input.cs"
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
        var errors = outputCompilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Location.SourceTree is not null)
            .ToArray();
        var generatedSource = string.Concat(
            outputCompilation.SyntaxTrees.Where(tree => tree != inputTree).Select(tree => tree.ToString())
        );

        return new GeneratorResult(
            pulseDiagnostics,
            [.. errors.Where(d => d.Location.SourceTree == inputTree)],
            [.. errors.Where(d => d.Location.SourceTree != inputTree)],
            generatedSource
        );
    }

    private sealed record GeneratorResult(
        Diagnostic[] PulseDiagnostics,
        Diagnostic[] InputErrors,
        Diagnostic[] GeneratedErrors,
        string GeneratedSource
    );
}
