# TimeWidget build & package script
# Produces a self-contained single-file TimeWidget.exe that runs on extract-and-click.
#
#   pwsh ./build.ps1                 # publish + stage dist/ + zip
#   pwsh ./build.ps1 -Install        # also copy the single exe to C:\Program Files\TimeWidget
#
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$InstallDir = 'C:\Program Files\TimeWidget',
    [switch]$Install
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'TimeWidget.csproj'
$publishDir = Join-Path $root 'publish'
$distDir = Join-Path $root 'dist'
$zipPath = Join-Path $root 'TimeWidget-win-x64.zip'

Write-Host "==> Cleaning previous output" -ForegroundColor Cyan
foreach ($d in @($publishDir, $distDir)) {
    if (Test-Path $d) { Remove-Item $d -Recurse -Force }
}
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Write-Host "==> Publishing self-contained single-file ($RuntimeIdentifier, $Configuration)" -ForegroundColor Cyan
dotnet publish $project `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

Write-Host "==> Staging dist/" -ForegroundColor Cyan
New-Item -ItemType Directory -Path $distDir -Force | Out-Null
Copy-Item (Join-Path $publishDir 'TimeWidget.exe') $distDir
Copy-Item (Join-Path $root 'README.md') $distDir -ErrorAction SilentlyContinue

Write-Host "==> Creating zip" -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $distDir '*') -DestinationPath $zipPath -Force

$exe = Get-Item (Join-Path $distDir 'TimeWidget.exe')
Write-Host ("    exe : {0} ({1:N1} MB)" -f $exe.FullName, ($exe.Length / 1MB)) -ForegroundColor Green
Write-Host ("    zip : {0} ({1:N1} MB)" -f $zipPath, ((Get-Item $zipPath).Length / 1MB)) -ForegroundColor Green

if ($Install) {
    Write-Host "==> Installing to $InstallDir" -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    Copy-Item (Join-Path $distDir 'TimeWidget.exe') (Join-Path $InstallDir 'TimeWidget.exe') -Force
    Write-Host ("    installed: {0}" -f (Join-Path $InstallDir 'TimeWidget.exe')) -ForegroundColor Green
}

Write-Host "Done." -ForegroundColor Green
