# U14 — Pin .NET 8 SDK at the repo root and attempt to build NetEvolve.Pulse.csproj.
# Expected: build FAILS with CS1003/CS8400/CS9999 on extension(...) member blocks.
# Run from repo root: pwsh audit/repros/u14/verify.ps1
#
# This script:
#   1. Backs up the existing global.json at repo root.
#   2. Copies the pinned-to-net8 global.json into the repo root.
#   3. Runs `dotnet build` on the core NetEvolve.Pulse project.
#   4. Restores the original global.json regardless of outcome.
#   5. Exits 0 if build succeeds (assumption REFUTED), exits 1 if build fails (assumption CONFIRMED).

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..\')
$originalGlobal = Join-Path $repoRoot 'global.json'
$pinnedGlobal = Join-Path $PSScriptRoot 'global.json'
$backupGlobal = Join-Path $PSScriptRoot 'global.json.repo-backup'

# Sanity check: .NET 8 SDK must be installed.
$sdks = & dotnet --list-sdks
if (-not ($sdks -match '^8\.')) {
    Write-Host 'U14 SKIPPED: .NET 8 SDK not installed on this machine.' -ForegroundColor Yellow
    exit 2
}

try {
    if (Test-Path $originalGlobal) {
        Copy-Item -Path $originalGlobal -Destination $backupGlobal -Force
    }
    Copy-Item -Path $pinnedGlobal -Destination $originalGlobal -Force

    Push-Location $repoRoot
    try {
        & dotnet --version | Out-Host
        & dotnet build src\NetEvolve.Pulse\NetEvolve.Pulse.csproj --nologo -v minimal 2>&1 | Tee-Object -FilePath (Join-Path $PSScriptRoot 'build.log') | Out-Host
        $exit = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($exit -eq 0) {
        Write-Host 'U14: BUILD SUCCEEDED on .NET 8 SDK — assumption REFUTED.' -ForegroundColor Green
        exit 0
    }
    else {
        Write-Host "U14: BUILD FAILED on .NET 8 SDK (exit=$exit) — assumption CONFIRMED." -ForegroundColor Red
        exit 1
    }
}
finally {
    if (Test-Path $backupGlobal) {
        Copy-Item -Path $backupGlobal -Destination $originalGlobal -Force
        Remove-Item -Path $backupGlobal -Force
    }
}
