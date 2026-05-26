# U14 Verification

**Status:** CONFIRMED (with nuance on the specific error code)

**Evidence:**
- `src/NetEvolve.Pulse/AssemblyScanningExtensions.cs:51` — `extension(IMediatorBuilder configurator) { ... }` (C# 14 extension block syntax).
- `Directory.Build.props:14-17` (this worktree) — multi-targets `net8.0;net9.0;net10.0` with no explicit `<LangVersion>`.
- `global.json` at repo root (pre-experiment) — no SDK pin.
- Live repro: pinned `global.json` to `8.0.421`, ran `dotnet build src/NetEvolve.Pulse/NetEvolve.Pulse.csproj`.
  - Captured log: `audit/repros/u14/build.log`.
  - Final lines: 2 errors, `error NETSDK1045: ... .NET 9.0 ...` for `NetEvolve.Pulse.csproj::TargetFramework=net9.0` and `NetEvolve.Pulse.Extensibility.csproj::TargetFramework=net9.0`.
- Same error when restricting `dotnet build -f net8.0` — MSBuild restores the full graph and the `net9.0` TFM of `NetEvolve.Pulse.Extensibility` (a project reference) still trips NETSDK1045.

**Reasoning:**
The assumption is CONFIRMED in spirit — `dotnet build` does NOT succeed on the .NET 8 SDK — but the actual blocking error is `NETSDK1045` (target framework not supported), not the C# 14 syntax errors `CS1003`/`CS8400` that the assumption predicted. The .NET 8 SDK refuses to resolve the `net9.0`/`net10.0` TFMs in the dependency graph before the compiler is even invoked, so the `extension(TReceiver)` syntax never has a chance to surface a parser error. **For a contributor with only the .NET 8 SDK installed, the result is identical: build fails, source contribution blocked.** The nuance is that the diagnostic is even less actionable than the assumption suggested — it says "install a newer SDK" without mentioning C# 14 or extension members.
A secondary, narrower confirmation: if `Directory.Build.props` were changed to remove `net9.0;net10.0` from `_ProjectTargetFrameworks`, only then would the C# 14 syntax CS1003/CS8400 errors emerge on the .NET 8 compiler. Since no maintainer is likely to make that change, the NETSDK1045 path is the observable failure.

**Failing test (if confirmed):**
- Path: `audit/repros/u14/`
- Status: written + executed (build log captured)
- Repro script: `audit/repros/u14/verify.ps1`
- Pinned SDK config: `audit/repros/u14/global.json`
- Captured build log (live evidence): `audit/repros/u14/build.log`
- Test code:
```powershell
# audit/repros/u14/verify.ps1 — see file for full source
# Summary:
#   1. Backs up the repo-root global.json.
#   2. Copies audit/repros/u14/global.json into the repo root (pins to 8.0.421, rollForward: disable).
#   3. Runs `dotnet build src/NetEvolve.Pulse/NetEvolve.Pulse.csproj`.
#   4. Restores the original global.json regardless of outcome.
#   5. Exits 0 if build succeeded (REFUTED) or 1 if it failed (CONFIRMED).
#
# Today: exits 1 (CONFIRMED).
```

Sample failing lines from `audit/repros/u14/build.log`:
```
C:\Program Files\dotnet\sdk\8.0.421\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.TargetFrameworkInference.targets(166,5):
    error NETSDK1045: Das aktuelle .NET SDK unterstützt .NET 9.0 nicht als Ziel. ...
    [...\NetEvolve.Pulse.Extensibility.csproj::TargetFramework=net9.0]
C:\Program Files\dotnet\sdk\8.0.421\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.TargetFrameworkInference.targets(166,5):
    error NETSDK1045: Das aktuelle .NET SDK unterstützt .NET 9.0 nicht als Ziel. ...
    [...\NetEvolve.Pulse\NetEvolve.Pulse.csproj::TargetFramework=net9.0]

0 Warnung(en)
2 Fehler
```

**Notes:**
- The error code is `NETSDK1045`, not `CS1003`/`CS8400` as the assumption predicted. Phase 3 should update the assumption text to reflect this, but the user-facing impact (contributor cannot build on the .NET 8 SDK) is identical.
- The C# 14 extension syntax is the deeper reason the project requires .NET 10 SDK; even if multi-target was reduced to net8.0 only, the language version inferred for net8.0 by the .NET 8 SDK would not include extension members. So either (a) keep the .NET 10 SDK requirement explicit via a `global.json` SDK pin and document it in CONTRIBUTING.md, or (b) refactor away from extension blocks for portability.
- The repro script restores `global.json` in a `finally` block, so running it does not contaminate the repo state.
- Original `global.json` (test runner config only, no SDK pin) is committed back into the repo state. The `audit/repros/u14/global.json.repo-backup` was created during verification and removed before commit.
