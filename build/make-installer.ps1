# One-shot installer build: publish the app into .\dist, then compile installer\totelabels.iss into
#   installer\Output\Tote-ally-Labeled-Setup-<version>.exe
# Run from anywhere:  pwsh -File build\make-installer.ps1 [-Ver 0.1.0]
param([string]$Ver)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# 1) Version: -Ver, else $env:TOTELABELS_VERSION (CI passes what release-version.ps1 decided), else the same rule.
$ver = if ($Ver) { $Ver }
       elseif ($env:TOTELABELS_VERSION) { $env:TOTELABELS_VERSION }
       else { & (Join-Path $PSScriptRoot 'release-version.ps1') }
Write-Host "== version $ver ==" -ForegroundColor Cyan

# 2) Locate the Inno Setup compiler (ISCC.exe) before spending time on the publish: PATH, then the standard
#    install locations (machine-wide, and the per-user install winget uses).
$iscc = (Get-Command 'iscc.exe' -ErrorAction SilentlyContinue | Select-Object -First 1).Source
if (-not $iscc) {
  foreach ($p in @(
      "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
      "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
      "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe")) {
    if (Test-Path $p) { $iscc = $p; break }
  }
}
if (-not $iscc) {
  throw "Inno Setup compiler (ISCC.exe) not found. Install it from https://jrsoftware.org/isdl.php, or add it to PATH."
}

# 3) Publish self-contained, so the PC it is installed on needs no .NET runtime. A clean folder each time,
#    so nothing from an older build ends up in the installer.
$dist = Join-Path $root 'dist'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
Write-Host "== publishing to $dist ==" -ForegroundColor Cyan
& dotnet publish (Join-Path $root 'ToteLabels.csproj') -c Release -r win-x64 --self-contained -o $dist "-p:ReleaseVersion=$ver"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# 4) Compile.
$iss = Join-Path $root 'installer\totelabels.iss'
Write-Host "== compiling installer ($iscc) ==" -ForegroundColor Cyan
& $iscc "/DAppVer=$ver" $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }

$out = Join-Path $root "installer\Output\Tote-ally-Labeled-Setup-$ver.exe"
Write-Host "== installer ready -> $out ==" -ForegroundColor Green
