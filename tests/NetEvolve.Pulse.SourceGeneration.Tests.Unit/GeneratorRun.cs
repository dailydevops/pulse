namespace NetEvolve.Pulse.SourceGeneration.Tests.Unit;

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

/// <summary>The result of a <see cref="GeneratorHarness"/> run.</summary>
internal sealed class GeneratorRun
{
    public GeneratorRun(
        GeneratorDriver driver,
        Compilation outputCompilation,
        SyntaxTree inputTree,
        ImmutableArray<Diagnostic> generatorDiagnostics
    )
    {
        Driver = driver;
        OutputCompilation = outputCompilation;
        PulseDiagnostics = [.. generatorDiagnostics.Where(d => d.Id.StartsWith("PULSE", StringComparison.Ordinal))];
        Sources = [.. driver.GetRunResult().Results.Single().GeneratedSources.Select(x => x.SourceText.ToString())];

        var errors = outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        InputErrors = [.. errors.Where(d => d.Location.SourceTree is null || d.Location.SourceTree == inputTree)];
        GeneratedErrors =
        [
            .. errors.Where(d => d.Location.SourceTree is not null && d.Location.SourceTree != inputTree),
        ];
    }

    /// <summary>The driver after the run, for incremental follow-up runs.</summary>
    public GeneratorDriver Driver { get; }

    /// <summary>The input compilation plus the generated sources.</summary>
    public Compilation OutputCompilation { get; }

    /// <summary>The generator diagnostics with a <c>PULSE</c> id.</summary>
    public ImmutableArray<Diagnostic> PulseDiagnostics { get; }

    /// <summary>The generated source texts.</summary>
    public ImmutableArray<string> Sources { get; }

    /// <summary>
    /// Compilation errors located in the input source, plus errors without a source location (for example a broken
    /// reference set), because those come from the test setup and not from the generator.
    /// </summary>
    public Diagnostic[] InputErrors { get; }

    /// <summary>Compilation errors located in the generated sources.</summary>
    public Diagnostic[] GeneratedErrors { get; }

    /// <summary>All generated source texts, concatenated.</summary>
    public string GeneratedSource => string.Concat(Sources);

    /// <summary>
    /// Throws when the output compilation has errors, so a snapshot of code that does not compile cannot be approved.
    /// </summary>
    public GeneratorRun EnsureCompiles()
    {
        if (InputErrors.Length > 0 || GeneratedErrors.Length > 0)
        {
            throw new InvalidOperationException(
                "The output compilation has errors:"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, InputErrors.Concat(GeneratedErrors))
            );
        }

        return this;
    }

    /// <summary>Emits the output compilation and loads it into a collectible <see cref="System.Runtime.Loader.AssemblyLoadContext"/>.</summary>
    public LoadedAssembly Load() => new(OutputCompilation);
}
