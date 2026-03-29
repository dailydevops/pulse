#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace NetEvolve.Pulse.Tests;

#pragma warning restore IDE0130 // Namespace does not match folder structure

using System.Runtime.CompilerServices;
using Argon;

internal static class Predefined
{
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

        VerifierSettings.AddExtraSettings(o =>
        {
            o.DefaultValueHandling = DefaultValueHandling.Ignore;
            o.NullValueHandling = NullValueHandling.Ignore;
        });
    }
}
