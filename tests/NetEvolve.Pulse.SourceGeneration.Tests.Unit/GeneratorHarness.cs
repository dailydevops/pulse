namespace NetEvolve.Pulse.SourceGeneration.Tests.Unit;

using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.SourceGeneration.Generators;

/// <summary>
/// Runs <see cref="PulseHandlerGenerator"/> on an in-memory compilation that references
/// Microsoft.Extensions.DependencyInjection and the Pulse assemblies. The output compilation is kept, so tests can
/// check that the generated code compiles and can load it to resolve the registered services.
/// </summary>
internal static class GeneratorHarness
{
    public const string DefaultAssemblyName = "TestAssembly";

    public static readonly CSharpParseOptions DefaultParseOptions = new(LanguageVersion.Latest);

    private static readonly MetadataReference[] BaseReferences = CreateBaseReferences();

    private static readonly MetadataReference[] PulseReferences =
    [
        .. BaseReferences,
        MetadataReference.CreateFromFile(typeof(NativeAotInterceptorExtensions).Assembly.Location),
    ];

    /// <summary>
    /// Returns the metadata references for a test compilation. <c>NetEvolve.Pulse</c> is opt-in, because the generator
    /// emits the NativeAOT interceptor registrations only when the compilation references it.
    /// </summary>
    public static MetadataReference[] GetReferences(bool referencePulse) =>
        referencePulse ? PulseReferences : BaseReferences;

    /// <summary>Parses <paramref name="source"/> and runs the generator on it.</summary>
    public static GeneratorRun Run(
        string source,
        string assemblyName = DefaultAssemblyName,
        string? rootNamespace = DefaultAssemblyName,
        bool referencePulse = false,
        CSharpParseOptions? parseOptions = null
    )
    {
        parseOptions ??= DefaultParseOptions;
        return Run(CSharpSyntaxTree.ParseText(source, parseOptions), assemblyName, rootNamespace, referencePulse);
    }

    /// <summary>Runs the generator on a compilation that contains only <paramref name="inputTree"/>.</summary>
    public static GeneratorRun Run(
        SyntaxTree inputTree,
        string assemblyName = DefaultAssemblyName,
        string? rootNamespace = DefaultAssemblyName,
        bool referencePulse = false
    )
    {
        var compilation = CreateCompilation(inputTree, assemblyName, referencePulse);

        var driver = CreateDriver(rootNamespace, (CSharpParseOptions)inputTree.Options)
            .RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);

        return new GeneratorRun(driver, outputCompilation, inputTree, generatorDiagnostics);
    }

    /// <summary>Creates a library compilation that contains only <paramref name="inputTree"/>.</summary>
    public static CSharpCompilation CreateCompilation(
        SyntaxTree inputTree,
        string assemblyName = DefaultAssemblyName,
        bool referencePulse = false
    ) =>
        CSharpCompilation.Create(
            assemblyName,
            [inputTree],
            GetReferences(referencePulse),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

    /// <summary>Creates a generator driver for <see cref="PulseHandlerGenerator"/>.</summary>
    public static GeneratorDriver CreateDriver(
        string? rootNamespace = DefaultAssemblyName,
        CSharpParseOptions? parseOptions = null
    ) =>
        CSharpGeneratorDriver.Create(
            generators: [new PulseHandlerGenerator().AsSourceGenerator()],
            optionsProvider: new TestAnalyzerConfigOptionsProvider(rootNamespace),
            parseOptions: parseOptions ?? DefaultParseOptions
        );

    private static MetadataReference[] CreateBaseReferences() =>
        [
            .. (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)!
                .Split(Path.PathSeparator)
                .Where(path =>
                {
                    var fileName = Path.GetFileName(path);
                    return fileName.StartsWith("System.", StringComparison.Ordinal)
                        || fileName.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal)
                        || string.Equals(fileName, "netstandard.dll", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(fileName, "mscorlib.dll", StringComparison.OrdinalIgnoreCase);
                })
                .Select(path => MetadataReference.CreateFromFile(path)),
            MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ServiceCollection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Extensibility.ICommand<>).Assembly.Location),
        ];
}
