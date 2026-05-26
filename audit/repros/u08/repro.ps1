#!/usr/bin/env pwsh
# U08 repro — pack NetEvolve.Pulse.SqlServer and assert OutboxMessage.sql is
# reachable from PackageReference consumers (contentFiles/, build/, or
# buildTransitive/). Today only content/Scripts/ exists, so the assertion fails.

$ErrorActionPreference = 'Stop'

# Locate repo root (Pulse.slnx is the marker).
$repoRoot = (Get-Item $PSScriptRoot).FullName
while ($repoRoot -and -not (Test-Path (Join-Path $repoRoot 'Pulse.slnx'))) {
    $repoRoot = (Get-Item $repoRoot).Parent?.FullName
}
if (-not $repoRoot) {
    Write-Error 'Could not locate Pulse.slnx — run this from inside the repo.'
    exit 2
}

$csproj = Join-Path $repoRoot 'src/NetEvolve.Pulse.SqlServer/NetEvolve.Pulse.SqlServer.csproj'
if (-not (Test-Path $csproj)) {
    Write-Error "Csproj not found: $csproj"
    exit 2
}

$outputDir = Join-Path ([System.IO.Path]::GetTempPath()) ("pulse-u08-" + [System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

try {
    Write-Host "Packing $csproj -> $outputDir"
    & dotnet pack $csproj -o $outputDir --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet pack failed with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }

    $nupkg = Get-ChildItem $outputDir -Filter 'NetEvolve.Pulse.SqlServer.*.nupkg' | Select-Object -First 1
    if (-not $nupkg) {
        Write-Error "No nupkg produced under $outputDir"
        exit 2
    }

    Write-Host "Inspecting $($nupkg.FullName)"

    Add-Type -AssemblyName 'System.IO.Compression.FileSystem' -ErrorAction SilentlyContinue
    $archive = [System.IO.Compression.ZipFile]::OpenRead($nupkg.FullName)
    try {
        $entries = $archive.Entries | ForEach-Object { $_.FullName -replace '\\', '/' }
        $sqlEntries = $entries | Where-Object { $_ -like '*/OutboxMessage.sql' }

        Write-Host ''
        Write-Host 'OutboxMessage.sql entries found:'
        if ($sqlEntries.Count -eq 0) {
            Write-Host '  (none)'
        }
        else {
            $sqlEntries | ForEach-Object { Write-Host "  - $_" }
        }
        Write-Host ''

        $reachable = $sqlEntries | Where-Object {
            $_ -like 'contentFiles/*' -or $_ -like 'build/*' -or $_ -like 'buildTransitive/*'
        }

        if ($reachable.Count -gt 0) {
            Write-Host "PASS — script is reachable from PackageReference consumers:"
            $reachable | ForEach-Object { Write-Host "  $_" }
            exit 0
        }
        else {
            Write-Host 'FAIL — OutboxMessage.sql is NOT reachable from PackageReference consumers.'
            Write-Host '  Only content/ (legacy packages.config) paths were found.'
            Write-Host '  Modern SDK-style consumers will not receive this file.'
            exit 1
        }
    }
    finally {
        $archive.Dispose()
    }
}
finally {
    if (Test-Path $outputDir) {
        Remove-Item $outputDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
