namespace NetEvolve.Pulse.SourceGeneration.Tests.Unit;

using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NetEvolve.Extensions.TUnit;
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
    [Arguments(
        "InternalHandlerForPrivateNestedMessage",
        """
            public class Outer
            {
                private sealed record Cmd : RequestBase, ICommand<string>;

                [PulseHandler]
                internal sealed class Blocked : ICommandHandler<Cmd, string>
                {
                    Task<string> ICommandHandler<Cmd, string>.HandleAsync(Cmd command, CancellationToken cancellationToken) => Task.FromResult(string.Empty);
                }
            }
            """
    )]
    [Arguments(
        "InternalHandlerForPrivateNestedResponse",
        """
            public class Outer
            {
                private sealed record Res;

                public sealed record Cmd : RequestBase, ICommand<Res>;

                [PulseHandler]
                internal sealed class Blocked : ICommandHandler<Cmd, Res>
                {
                    Task<Res> ICommandHandler<Cmd, Res>.HandleAsync(Cmd command, CancellationToken cancellationToken) => Task.FromResult(new Res());
                }
            }
            """
    )]
    [Arguments(
        "InternalHandlerForPrivateTypeArgumentOfResponse",
        """
            public class Outer
            {
                private sealed record Item;

                public sealed record Cmd : RequestBase, ICommand<System.Collections.Generic.List<Item>>;

                [PulseHandler]
                internal sealed class Blocked : ICommandHandler<Cmd, System.Collections.Generic.List<Item>>
                {
                    Task<System.Collections.Generic.List<Item>> ICommandHandler<Cmd, System.Collections.Generic.List<Item>>.HandleAsync(Cmd command, CancellationToken cancellationToken) => Task.FromResult(new System.Collections.Generic.List<Item>());
                }
            }
            """
    )]
    [Arguments(
        "InternalHandlerForPrivateNestedEvent",
        """
            public class Outer
            {
                private sealed record Evt : RequestBase, IEvent
                {
                    public string Id { get; init; } = string.Empty;
                    public DateTimeOffset? PublishedAt { get; set; }
                }

                [PulseHandler]
                internal sealed class Blocked : IEventHandler<Evt>
                {
                    Task IEventHandler<Evt>.HandleAsync(Evt message, CancellationToken cancellationToken) => Task.CompletedTask;
                }
            }
            """
    )]
    [Arguments(
        "ExplicitGenericHandlerForPrivateNestedMessage",
        """
            public class Outer
            {
                private sealed record Cmd : RequestBase, ICommand<string>;

                [PulseHandler<Cmd>]
                internal sealed class Blocked<TCmd> : ICommandHandler<TCmd, string>
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
            _ = await Assert.That(IsReportedOnBlockedDeclaration(result)).IsTrue().Because(scenario);
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
            _ = await Assert.That(IsReportedOnBlockedDeclaration(result)).IsTrue().Because(scenario);
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
            _ = await Assert.That(IsReportedOnBlockedDeclaration(result)).IsTrue().Because(scenario);
            _ = await Assert
                .That(result.PulseDiagnostics.Select(d => d.GetMessage(CultureInfo.InvariantCulture)))
                .All(message => message.Contains("nested in a generic type", StringComparison.Ordinal))
                .Because(scenario);
            _ = await Assert
                .That(result.PulseDiagnostics.Select(d => d.GetMessage(CultureInfo.InvariantCulture)))
                .All(message => !message.Contains("[PulseHandler]", StringComparison.Ordinal))
                .Because(scenario);
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
    [Arguments(
        "InternalHandlerForInternalNestedMessage",
        """
            public class Outer
            {
                internal sealed record Cmd : RequestBase, ICommand<string>;

                [PulseHandler]
                internal sealed class Allowed : ICommandHandler<Cmd, string>
                {
                    public Task<string> HandleAsync(Cmd command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
                }
            }
            """,
        "global::Outer.Allowed"
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
    [Arguments(
        "InternalHandlerForPrivateNestedMessage",
        """
            public class Outer
            {
                private sealed record Cmd : RequestBase, ICommand<string>;

                internal sealed class Skipped : ICommandHandler<Cmd, string>
                {
                    Task<string> ICommandHandler<Cmd, string>.HandleAsync(Cmd command, CancellationToken cancellationToken) => Task.FromResult(string.Empty);
                }
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

    private static GeneratorRun RunGenerator(string declarations) =>
        GeneratorHarness.Run(
            CSharpSyntaxTree.ParseText(Preamble + declarations, GeneratorHarness.DefaultParseOptions, path: "Input.cs")
        );

    private static bool IsReportedOnBlockedDeclaration(GeneratorRun result)
    {
        var inputTree = result.OutputCompilation.SyntaxTrees.Single(tree => tree.FilePath == "Input.cs");
        var blockedSpan = inputTree
            .GetRoot()
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Single(declaration => declaration.Identifier.ValueText == "Blocked")
            .Span;

        return result.PulseDiagnostics.All(diagnostic =>
            string.Equals(diagnostic.Location.GetLineSpan().Path, inputTree.FilePath, StringComparison.Ordinal)
            && blockedSpan.Contains(diagnostic.Location.SourceSpan)
        );
    }
}
