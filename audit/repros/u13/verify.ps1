# U13 — NuGet metadata: assert <icon> is present and <readme> is present in the generated nuspec.
# Run from repo root: pwsh audit/repros/u13/verify.ps1
#
# Expected state today:
#   - On .NET SDK 10.x: FAILS on <icon> only (SDK auto-detects README.md adjacent to csproj).
#   - On .NET SDK 8.x:  FAILS on BOTH <icon> and <readme>.

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..\')
$projectPath = Join-Path $repoRoot 'src\NetEvolve.Pulse.Extensibility\NetEvolve.Pulse.Extensibility.csproj'
$outDir = Join-Path $PSScriptRoot 'pack-out'
$null = New-Item -ItemType Directory -Force -Path $outDir

# Build then pack (pack alone won't rebuild dependencies reliably)
& dotnet build $projectPath -c Release --nologo -v minimal | Out-Host
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

# Locate the produced nupkg
$nupkg = Get-ChildItem (Join-Path $repoRoot 'src\NetEvolve.Pulse.Extensibility\bin\Release') -Filter '*.nupkg' |
    Where-Object { $_.Name -notlike '*.symbols.nupkg' } |
    Select-Object -First 1
if (-not $nupkg) { throw "No .nupkg produced" }

# Extract the nuspec
$extracted = Join-Path $outDir 'extracted'
if (Test-Path $extracted) { Remove-Item -Recurse -Force $extracted }
$null = New-Item -ItemType Directory -Force -Path $extracted
Expand-Archive -Path $nupkg.FullName -DestinationPath $extracted -Force

$nuspecPath = Get-ChildItem $extracted -Filter '*.nuspec' | Select-Object -First 1
if (-not $nuspecPath) { throw "nuspec not found inside .nupkg" }

[xml]$nuspec = Get-Content $nuspecPath.FullName
$ns = New-Object System.Xml.XmlNamespaceManager($nuspec.NameTable)
$ns.AddNamespace('n', 'http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd')

$iconNode = $nuspec.SelectSingleNode('/n:package/n:metadata/n:icon', $ns)
$readmeNode = $nuspec.SelectSingleNode('/n:package/n:metadata/n:readme', $ns)

$failures = @()
if (-not $iconNode) { $failures += 'U13: <icon> missing from nuspec (logo.png at repo root is not packed).' }
if (-not $readmeNode) { $failures += 'U13: <readme> missing from nuspec.' }

if ($failures.Count -gt 0) {
    Write-Host '--- U13 ASSERTION FAILURES ---' -ForegroundColor Red
    $failures | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    exit 1
}

Write-Host 'U13: Both <icon> and <readme> are present in the nuspec.' -ForegroundColor Green
exit 0
