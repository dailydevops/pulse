namespace NetEvolve.Pulse.SourceGeneration.Tests.Unit;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetEvolve.Extensions.TUnit;
using TUnit.Core;

[TestGroup("SourceGeneration")]
[TestGroup("SourceGeneration.PulseHandler")]
public class GeneratorRunTests
{
    [Test]
    public async Task WhenErrorHasNoSourceLocationThenEnsureCompilesThrows(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var inputTree = CSharpSyntaxTree.ParseText(
            "public class Empty;",
            GeneratorHarness.DefaultParseOptions,
            cancellationToken: cancellationToken
        );
        var compilation = GeneratorHarness
            .CreateCompilation(inputTree)
            .WithOptions(new CSharpCompilationOptions(OutputKind.ConsoleApplication));

        var driver = GeneratorHarness
            .CreateDriver()
            .RunGeneratorsAndUpdateCompilation(
                compilation,
                out var outputCompilation,
                out var generatorDiagnostics,
                cancellationToken
            );
        var run = new GeneratorRun(driver, outputCompilation, inputTree, generatorDiagnostics);

        _ = await Assert.That(run.InputErrors.Select(d => d.Id)).Contains("CS5001");
        _ = await Assert.That(() => run.EnsureCompiles()).Throws<InvalidOperationException>();
    }
}
