namespace NetEvolve.Pulse.SourceGeneration.Tests.Unit;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.SourceGeneration.Generators;
using TUnit.Core;

[TestGroup("SourceGeneration")]
[TestGroup("SourceGeneration.PulseHandler")]
public class PulseHandlerGeneratorIncrementalTests
{
    [Test]
    public async Task WhenLinesInsertedAboveUnannotatedHandlerThenPulse003LocationFollowsDeclaration()
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

        var (driver, compilation, tree) = RunInitialGeneration(source);

        var editedTree = CSharpSyntaxTree.ParseText(
            "// line one" + Environment.NewLine + "// line two" + Environment.NewLine + source,
            path: "TestFile.cs"
        );
        var editedCompilation = compilation.ReplaceSyntaxTree(tree, editedTree);
        driver = driver.RunGeneratorsAndUpdateCompilation(editedCompilation, out _, out _);
        var diagnostics = driver.GetRunResult().Results.Single().Diagnostics;

        var diagnostic = diagnostics.Single(d => string.Equals(d.Id, "PULSE003", StringComparison.Ordinal));
        var editedRoot = await editedTree.GetRootAsync().ConfigureAwait(false);
        var expectedPosition = editedRoot
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Single()
            .GetLocation()
            .GetLineSpan()
            .StartLinePosition;

        _ = await Assert.That(diagnostic.Location.GetLineSpan().StartLinePosition).IsEqualTo(expectedPosition);
    }

    [Test]
    public async Task WhenLinesInsertedAboveInvalidExplicitMessageTypeThenPulse005LocationFollowsDeclaration()
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

        var (driver, compilation, tree) = RunInitialGeneration(source);

        var editedTree = CSharpSyntaxTree.ParseText(
            "// line one" + Environment.NewLine + "// line two" + Environment.NewLine + source,
            path: "TestFile.cs"
        );
        var editedCompilation = compilation.ReplaceSyntaxTree(tree, editedTree);
        driver = driver.RunGeneratorsAndUpdateCompilation(editedCompilation, out _, out _);
        var diagnostics = driver.GetRunResult().Results.Single().Diagnostics;

        var diagnostic = diagnostics.Single(d => string.Equals(d.Id, "PULSE005", StringComparison.Ordinal));
        var editedRoot = await editedTree.GetRootAsync().ConfigureAwait(false);
        var expectedPosition = editedRoot
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Single()
            .GetLocation()
            .GetLineSpan()
            .StartLinePosition;

        _ = await Assert.That(diagnostic.Location.GetLineSpan().StartLinePosition).IsEqualTo(expectedPosition);
    }

    private static (GeneratorDriver Driver, CSharpCompilation Compilation, SyntaxTree Tree) RunInitialGeneration(
        string source
    )
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: "TestFile.cs");
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [tree],
            GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var optionsProvider = new TestAnalyzerConfigOptionsProvider("TestAssembly");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new PulseHandlerGenerator().AsSourceGenerator()],
            optionsProvider: optionsProvider
        );

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return (driver, compilation, tree);
    }

    private static MetadataReference[] GetMetadataReferences()
    {
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

        runtimeReferences.Add(MetadataReference.CreateFromFile(typeof(Extensibility.ICommand<>).Assembly.Location));
        runtimeReferences.Add(
            MetadataReference.CreateFromFile(typeof(Extensibility.Attributes.PulseHandlerAttribute).Assembly.Location)
        );

        return [.. runtimeReferences];
    }
}
