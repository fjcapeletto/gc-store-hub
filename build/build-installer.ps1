<#
.SYNOPSIS
  Build the GC Store hub into a Velopack installer + update feed, locally.

.DESCRIPTION
  Mirrors what the CI workflow (.github/workflows/release.yml) does, for local runs.
  Produces artifacts/releases/GabrielCapelettoStoreHub-win-Setup.exe.

.PREREQUISITES
  - .NET 10 SDK
  - Velopack CLI:  dotnet tool install -g vpk --version 1.2.0

.EXAMPLE
  ./build/build-installer.ps1 -Version 1.0.1
#>
param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$root      = Split-Path -Parent $PSScriptRoot
$project   = Join-Path $root "src/GabrielCapelettoStore.Hub/GabrielCapelettoStore.Hub.csproj"
$icon      = Join-Path $root "src/GabrielCapelettoStore.Hub/Assets/gcstore.ico"
$publish   = Join-Path $root "artifacts/publish"
$releases  = Join-Path $root "artifacts/releases"

if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }

Write-Host "==> Publishing (self-contained win-x64)..." -ForegroundColor Cyan
dotnet publish $project -c Release -r win-x64 --self-contained true -o $publish

Write-Host "==> Packing with Velopack (v$Version)..." -ForegroundColor Cyan
vpk pack `
    -u GabrielCapelettoStoreHub `
    -v $Version `
    -p $publish `
    -e GabrielCapelettoStore.Hub.exe `
    --packTitle "Gabriel Capeletto Store" `
    --packAuthors "GabrielCapeletto LLC" `
    --icon $icon `
    -o $releases

Write-Host "==> Done. Installer:" -ForegroundColor Green
Write-Host "    $releases\GabrielCapelettoStoreHub-win-Setup.exe"
