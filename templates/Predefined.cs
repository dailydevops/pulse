#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace NetEvolve.Pulse.Tests;

#pragma warning restore IDE0130 // Namespace does not match folder structure

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Argon;

/// <summary>
/// Module-level Verify configuration applied to all test assemblies that reference this template.
/// </summary>
internal static partial class Predefined
{
    /// <summary>
    /// Configures Verify snapshot paths, auto-verify behavior, JSON serialization settings,
    /// and line scrubbers for the current test assembly.
    /// </summary>
    [ModuleInitializer]
    public static void Init()
    {
        DerivePathInfo(
            (sourceFile, projectDirectory, type, method) =>
            {
                var relativePath =
                    Path.GetDirectoryName(sourceFile) is string sourceDirectory
                    && !projectDirectory.Equals(sourceDirectory, StringComparison.OrdinalIgnoreCase)
                        ? sourceDirectory.Replace(
                            projectDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                            string.Empty,
                            StringComparison.OrdinalIgnoreCase
                        )
                        : string.Empty;
                var directory = Path.Combine(projectDirectory, "_snapshots", relativePath);
                _ = Directory.CreateDirectory(directory);
                return new(directory, type.Name, method.Name);
            }
        );

        VerifierSettings.AutoVerify(includeBuildServer: false, throwException: true);
        VerifierSettings.SortJsonObjects();
        VerifierSettings.SortPropertiesAlphabetically();

        VerifierSettings.ScrubLinesWithReplace(line =>
            ScrubLangVersion().Replace(line, "LanguageVersion: CSharpLatest")
        );

        VerifierSettings.ScrubLinesWithReplace(line => ScrubGeneatedCodeVersion().Replace(line, "$1{version}$2"));

        VerifierSettings.AddExtraSettings(o =>
        {
            o.DefaultValueHandling = DefaultValueHandling.Ignore;
            o.NullValueHandling = NullValueHandling.Ignore;
        });
    }

    /// <summary>Matches <c>LanguageVersion: CSharp&lt;N&gt;</c> tokens for version-agnostic scrubbing.</summary>
    [GeneratedRegex(@"LanguageVersion: CSharp\d+")]
    private static partial Regex ScrubLangVersion();

    /// <summary>
    /// Matches the version token inside <c>[GeneratedCode("NetEvolve.Pulse.SourceGeneration", "…")]</c>
    /// for version-agnostic scrubbing.
    /// </summary>
    [GeneratedRegex(@"(GeneratedCode\(""NetEvolve\.Pulse\.SourceGeneration"", "")[^""]+("")")]
    private static partial Regex ScrubGeneatedCodeVersion();
}
