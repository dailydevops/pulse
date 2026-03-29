namespace NetEvolve.Pulse.SourceGeneration.Tests.Unit;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Test implementation of <see cref="AnalyzerConfigOptionsProvider"/> that allows
/// specifying build properties like <c>RootNamespace</c>.
/// </summary>
internal sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
{
    private readonly TestAnalyzerConfigOptions _globalOptions;

    public TestAnalyzerConfigOptionsProvider(string? rootNamespace = null) =>
        _globalOptions = new TestAnalyzerConfigOptions(rootNamespace);

    public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => TestAnalyzerConfigOptions.Empty;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => TestAnalyzerConfigOptions.Empty;
}

/// <summary>
/// Test implementation of <see cref="AnalyzerConfigOptions"/> backed by a dictionary.
/// </summary>
internal sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
{
    public static readonly TestAnalyzerConfigOptions Empty = new(rootNamespace: null);

    private readonly Dictionary<string, string> _options;

    public TestAnalyzerConfigOptions(string? rootNamespace)
    {
        _options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (rootNamespace is not null)
        {
            _options["build_property.RootNamespace"] = rootNamespace;
        }
    }

    public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) =>
        _options.TryGetValue(key, out value);
}
