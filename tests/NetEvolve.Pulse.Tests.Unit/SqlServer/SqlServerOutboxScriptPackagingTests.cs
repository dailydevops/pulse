namespace NetEvolve.Pulse.Tests.Unit.SqlServer;

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// Phase 2 audit U08: <c>OutboxMessage.sql</c> must be reachable from PackageReference
/// consumers. Today it is packed under <c>content\Scripts\</c> (legacy packages.config
/// mechanism) which does not flow to modern SDK-style consumers.
/// See <c>audit/verification/round-01-U08.md</c>.
/// </summary>
[TestGroup("SqlServer")]
public sealed class SqlServerOutboxScriptPackagingTests
{
    [Test]
    [SuppressMessage(
        "Performance",
        "CA1849:Call async methods when in an async method",
        Justification = "ZipFile.OpenReadAsync is .NET 10 only; this audit repro must build on net8/net9 too."
    )]
    [SuppressMessage(
        "Major Code Smell",
        "S6966:Awaitable method should be used",
        Justification = "Same reason as CA1849 — ZipFile.OpenReadAsync is not available on all TFMs."
    )]
    public async Task OutboxMessage_sql_must_be_reachable_from_PackageReference_consumers()
    {
        // ARRANGE — Locate the SqlServer csproj relative to repo root.
        var repoRoot = LocateRepoRoot();
        var csproj = Path.Combine(repoRoot, "src", "NetEvolve.Pulse.SqlServer", "NetEvolve.Pulse.SqlServer.csproj");

        _ = await Assert.That(File.Exists(csproj)).IsTrue();

        // ACT — Pack into a scratch folder.
        var outputDir = Path.Combine(Path.GetTempPath(), $"pulse-u08-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(outputDir);

        try
        {
            var psi = new ProcessStartInfo("dotnet", $"pack \"{csproj}\" -o \"{outputDir}\" --nologo")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using var process = Process.Start(psi)!;
            await process.WaitForExitAsync().ConfigureAwait(false);

            _ = await Assert.That(process.ExitCode).IsEqualTo(0);

            // ASSERT — The nupkg must expose OutboxMessage.sql via contentFiles/ or
            // build*/buildTransitive/ (anything that flows to PackageReference consumers).
            // content\ alone does NOT — that is the legacy packages.config path.
            var nupkg = Directory.EnumerateFiles(outputDir, "NetEvolve.Pulse.SqlServer.*.nupkg").FirstOrDefault();

            _ = await Assert.That(nupkg).IsNotNull();

            using var archive = ZipFile.OpenRead(nupkg!);
            var entries = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToArray();

            var reachableFromPackageReference = entries.Any(e =>
                e.EndsWith("/OutboxMessage.sql", StringComparison.OrdinalIgnoreCase)
                && (
                    e.StartsWith("contentFiles/", StringComparison.OrdinalIgnoreCase)
                    || e.StartsWith("build/", StringComparison.OrdinalIgnoreCase)
                    || e.StartsWith("buildTransitive/", StringComparison.OrdinalIgnoreCase)
                )
            );

            _ = await Assert.That(reachableFromPackageReference).IsTrue();
        }
        finally
        {
            try
            {
                Directory.Delete(outputDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Pulse.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Pulse.slnx not located.");
    }
}
