# U08 Verification

**Status:** CONFIRMED

**Evidence:**
- `src/NetEvolve.Pulse/Outbox/OutboxOptions.cs:18` (`public string Schema { get; set; } = "pulse";`).
- `src/NetEvolve.Pulse.SqlServer/NetEvolve.Pulse.SqlServer.csproj:18` — only packaging line is `<None Include="Scripts\*.sql" Pack="true" PackagePath="content\Scripts" />` (legacy `content\` mechanism — `packages.config` only; does NOT flow to PackageReference consumers).
- `src/NetEvolve.Pulse.SqlServer/SqlServerExtensions.cs:23-25` — XML doc: *"Execute the schema script from `Scripts/OutboxMessage.sql`"* — script not delivered.
- `src/NetEvolve.Pulse.SqlServer/Scripts/OutboxMessage.sql` — exists in the repo, but `content\Scripts` is the only `Pack=true` path.

**Reasoning:** The `content\` folder in a NuGet package only flows to `packages.config`-style consumers. For `PackageReference`-based projects (the modern SDK-style default since .NET Core), files must live under `contentFiles\<lang>\<tfm>\…` and be paired with `PackageCopyToOutput`/`BuildAction` metadata, or be delivered via `build\` / `buildTransitive\` MSBuild props/targets. Modern SDK-style consumers receive nothing from `content\`. The `EmbeddedResource Include="Scripts\*.sql"` line on `csproj:19` does embed the file into the assembly, but the README and XML docs tell users to **execute the file on disk** — there is no documented `Assembly.GetManifestResourceStream(...)` retrieval helper.

**Failing repro:**
- Path: `audit/repros/u08/`
- Status: written

The repro packs `NetEvolve.Pulse.SqlServer` and asserts the resulting `.nupkg` exposes `OutboxMessage.sql` at a `PackageReference`-reachable path. Today the only path used is `content\Scripts\OutboxMessage.sql`, which fails the assertion.

A companion TUnit test (`tests/NetEvolve.Pulse.Tests.Unit/SqlServer/SqlServerOutboxScriptPackagingTests.cs`) packs the package on demand and inspects it; this runs in CI and fails today.

**Failing test code (TUnit, `tests/.../SqlServerOutboxScriptPackagingTests.cs`):**

```csharp
namespace NetEvolve.Pulse.Tests.Unit.SqlServer;

using System;
using System.Diagnostics;
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
/// </summary>
[TestGroup("SqlServer")]
public sealed class SqlServerOutboxScriptPackagingTests
{
    [Test]
    public async Task OutboxMessage_sql_must_be_reachable_from_PackageReference_consumers()
    {
        // ARRANGE — Locate the SqlServer csproj relative to repo root.
        var repoRoot = LocateRepoRoot();
        var csproj = Path.Combine(
            repoRoot,
            "src",
            "NetEvolve.Pulse.SqlServer",
            "NetEvolve.Pulse.SqlServer.csproj"
        );

        _ = await Assert.That(File.Exists(csproj)).IsTrue();

        // ACT — Pack into a scratch folder.
        var outputDir = Path.Combine(Path.GetTempPath(), $"pulse-u08-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(outputDir);

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
        var nupkg = Directory.EnumerateFiles(outputDir, "NetEvolve.Pulse.SqlServer.*.nupkg").First();

        using var archive = ZipFile.OpenRead(nupkg);
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
```

**Notes:**
- The PowerShell repro at `audit/repros/u08/repro.ps1` is provided as a standalone, CI-independent demonstration that the SQL script is missing from PackageReference consumers. It also can run on Linux via `pwsh`.
- Phase 3 fix is small: change the csproj packaging line to one of:
  - `<None Include="Scripts\*.sql" Pack="true" PackagePath="contentFiles\any\any\Scripts" />` (with `BuildAction=None`, `CopyToOutput=true`) — copies to consumer output;
  - Or ship a `build\NetEvolve.Pulse.SqlServer.targets` that copies the script as a build event;
  - Or document `Assembly.GetManifestResourceStream` retrieval (the script is already embedded — line 19 of the csproj) and provide a helper method that writes it to disk on demand.
- The same defect exists for `NetEvolve.Pulse.MySql`, `NetEvolve.Pulse.PostgreSql`, and `NetEvolve.Pulse.SQLite` SQL scripts — out of scope for U08 but worth a single shared fix.
- The TUnit test invokes `dotnet pack` from inside the test process, which is heavy but acceptable for an audit-discovery test that runs once. Phase 3 may move this assertion to a build-time MSBuild target.
