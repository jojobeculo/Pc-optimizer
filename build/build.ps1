<#
  FrameCore build script for Windows (PowerShell 5.1+ or 7+).
  Produces publish\FrameCore (self-contained win-x64) and artifacts\FrameCore-Setup-<ver>-x64.exe.
  Requirements: .NET SDK 10 (https://dotnet.microsoft.com), NSIS 3 (https://nsis.sourceforge.io).
  Optional: pandoc for HTML documentation.
  Usage: powershell -ExecutionPolicy Bypass -File build\build.ps1 [-SkipTests] [-NoInstaller]
#>
param([switch]$SkipTests, [switch]$NoInstaller)
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

[xml]$props = Get-Content (Join-Path $Root 'Directory.Build.props')
$Version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$Pub = Join-Path $Root 'publish\FrameCore'
$Art = Join-Path $Root 'artifacts'
Write-Host "== FrameCore $Version ==" -ForegroundColor Cyan

if (-not $SkipTests) {
  Write-Host '== Unit tests ==' -ForegroundColor Cyan
  dotnet test tests\FrameCore.Tests\FrameCore.Tests.csproj -c Release --nologo
  if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
}

Write-Host '== Publish ==' -ForegroundColor Cyan
if (Test-Path $Pub) { Remove-Item $Pub -Recurse -Force }
dotnet publish src\FrameCore.UI\FrameCore.UI.csproj -c Release -r win-x64 --self-contained true -o $Pub --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }

Write-Host '== Documentation ==' -ForegroundColor Cyan
New-Item -ItemType Directory -Force (Join-Path $Pub 'docs') | Out-Null
Copy-Item README.md, LICENSE $Pub
Copy-Item docs\*.md (Join-Path $Pub 'docs')
if (Get-Command pandoc -ErrorAction SilentlyContinue) {
  Get-ChildItem (Join-Path $Pub 'docs\*.md'), (Join-Path $Pub 'README.md') | ForEach-Object {
    pandoc --standalone --metadata title="$($_.BaseName)" -f gfm -t html5 --css=doc.css $_.FullName -o ([IO.Path]::ChangeExtension($_.FullName, '.html'))
  }
  Copy-Item build\doc.css (Join-Path $Pub 'docs\doc.css'); Copy-Item build\doc.css (Join-Path $Pub 'doc.css')
}

# The package must never contain per-machine data (what lives in %LOCALAPPDATA%\FrameCore on a PC).
$bad = Get-ChildItem $Pub -Recurse -Include settings.json, *.journal.json, *.log, profile.json, gpu-profiles.json, *.etl | Select-Object -First 1
if (-not $bad) { $bad = Get-ChildItem $Pub -Recurse -Directory | Where-Object { $_.Name -in 'Backups', 'Benchmarks', 'Reports', 'Logs', 'Profiles', 'QuickBench', 'Verification' } | Select-Object -First 1 }
if ($bad) { throw "Per-machine data found in publish folder: $($bad.FullName)" }
# No absolute path of the build machine may remain in the binaries (deterministic, path-mapped Release build).
$leak = Get-ChildItem $Pub -Recurse -File -Include *.dll, *.exe | Where-Object { Select-String -Path $_.FullName -SimpleMatch -Pattern $Root -Quiet } | Select-Object -First 1
if ($leak) { throw "Build machine path found in $($leak.FullName)" }
foreach ($p in 'FrameCore.exe', 'catalog\tweaks', 'lang\fr.json') { if (-not (Test-Path (Join-Path $Pub $p))) { throw "Missing $p" } }

if (-not $NoInstaller) {
  Write-Host '== Installer ==' -ForegroundColor Cyan
  $makensis = (Get-Command makensis -ErrorAction SilentlyContinue).Source
  if (-not $makensis) { $makensis = Join-Path ${env:ProgramFiles(x86)} 'NSIS\makensis.exe' }
  if (-not (Test-Path $makensis)) { throw 'makensis not found: install NSIS 3' }
  New-Item -ItemType Directory -Force $Art | Out-Null
  $Out = Join-Path $Art "FrameCore-Setup-$Version-x64.exe"
  & $makensis -V2 "-DVERSION=$Version" "-DSOURCE_DIR=$Pub" "-DOUT_FILE=$Out" installer\FrameCore.nsi
  if ($LASTEXITCODE -ne 0) { throw 'makensis failed' }
  $h = (Get-FileHash $Out -Algorithm SHA256).Hash.ToLowerInvariant()
  "$h  $(Split-Path $Out -Leaf)" | Set-Content (Join-Path $Art 'SHA256SUMS.txt') -Encoding ascii
  Get-ChildItem $Art
}
Write-Host '== Done ==' -ForegroundColor Green
