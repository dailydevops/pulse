namespace NetEvolve.Pulse.SourceGeneration.Tests.Unit;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

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
