#requires -Version 5.1

[CmdletBinding()]
param(
    [ValidateSet('All', 'Launcher', 'Installer')]
    [string]$Target = 'All'
)

$ErrorActionPreference = 'Stop'

$root             = $PSScriptRoot
$launcherProject  = Join-Path $root 'src\VECLauncher\VECLauncher.csproj'
$installerProject = Join-Path $root 'src\VECLauncher.Installer\VECLauncher.Installer.csproj'
$outDir           = Join-Path $root 'dist'
$tempPubLauncher  = Join-Path $env:TEMP 'veclauncher_publish'
$tempPubInstaller = Join-Path $env:TEMP 'vecinstaller_publish'

Write-Host ''
Write-Host '===================================================' -ForegroundColor Green
Write-Host " VEC Launcher :: Build Target: $Target" -ForegroundColor Green
Write-Host '===================================================' -ForegroundColor Green
Write-Host ''

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue

if (-not $dotnet) {
    $candidates = @(
        "$env:ProgramFiles\dotnet\dotnet.exe",
        "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { $dotnet = $c; break }
    }
}
else { $dotnet = $dotnet.Source }

if (-not $dotnet) {
    Write-Host '.NET SDK not found. Installing .NET 8 SDK...' -ForegroundColor Yellow

    $installer = Join-Path $env:TEMP 'dotnet-install.ps1'
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer -UseBasicParsing
    & $installer -Channel 8.0 -Quality GA -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet"

    $dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
    $env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"
}

Write-Host "dotnet: $dotnet" -ForegroundColor DarkGray
& $dotnet --version

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# ----------------------------------------------------
# 1. BUILD LAUNCHER
# ----------------------------------------------------
if ($Target -in @('All', 'Launcher')) {
    Write-Host ''
    Write-Host '---------------------------------------------------' -ForegroundColor Cyan
    Write-Host 'Publishing VECLauncher.exe (win-x64 Single-File)...' -ForegroundColor Cyan
    Write-Host '---------------------------------------------------' -ForegroundColor Cyan

    if (Test-Path $tempPubLauncher) { Remove-Item $tempPubLauncher -Recurse -Force }

    & $dotnet publish $launcherProject `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:IncludeAllContentForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none `
        -p:GenerateDocumentationFile=false `
        -o $tempPubLauncher

    if ($LASTEXITCODE -ne 0) {
        Write-Host 'Launcher build failed.' -ForegroundColor Red
        exit $LASTEXITCODE
    }

    $launcherExe = Join-Path $tempPubLauncher 'VECLauncher.exe'
    if (-not (Test-Path $launcherExe)) { throw "VECLauncher.exe not found in $tempPubLauncher" }

    $destLauncher = Join-Path $outDir 'VECLauncher.exe'
    Copy-Item $launcherExe -Destination $destLauncher -Force

    $webAppDownloads = Join-Path $root 'VyacheslavsMegaWebApp-dev\VyacheslavsMegaWebApp-dev\VyacheslavsMegaWebApp\wwwroot\downloads'
    if (Test-Path $webAppDownloads) {
        Copy-Item $destLauncher (Join-Path $webAppDownloads 'VECLauncher.exe') -Force -ErrorAction SilentlyContinue
    }

    # Create zip archive for bootstrap installer downloading
    $zipPath = Join-Path $outDir 'VECLauncher-win-x64.zip'
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path $destLauncher -DestinationPath $zipPath -CompressionLevel Optimal

    # Calculate SHA-256 for release manifest
    $sha256 = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $zipSize = (Get-Item $zipPath).Length

    # Create manifest.json
    $manifest = [ordered]@{
        version     = "1.0.0"
        downloadUrl = "/downloads/VECLauncher-win-x64.zip"
        sha256      = $sha256
        fileSize    = $zipSize
        changelog   = "Initial bootstrap release"
    }
    $manifestJson = $manifest | ConvertTo-Json -Depth 4
    $manifestPath = Join-Path $outDir 'manifest.json'
    Set-Content -Path $manifestPath -Value $manifestJson -Encoding UTF8

    if (Test-Path $webAppDownloads) {
        Copy-Item $manifestPath (Join-Path $webAppDownloads 'manifest.json') -Force -ErrorAction SilentlyContinue
        Copy-Item $zipPath (Join-Path $webAppDownloads 'VECLauncher-win-x64.zip') -Force -ErrorAction SilentlyContinue
    }
}

# ----------------------------------------------------
# 2. BUILD INSTALLER
# ----------------------------------------------------
if ($Target -in @('All', 'Installer')) {
    Write-Host ''
    Write-Host '---------------------------------------------------' -ForegroundColor Cyan
    Write-Host 'Building VEC_Setup.exe (Standalone Web Bootstrapper)...' -ForegroundColor Cyan
    Write-Host '---------------------------------------------------' -ForegroundColor Cyan

    & $dotnet build $installerProject -c Release -p:DebugType=none -p:GenerateDocumentationFile=false

    if ($LASTEXITCODE -ne 0) {
        Write-Host 'Installer build failed.' -ForegroundColor Red
        exit $LASTEXITCODE
    }

    $setupExe = Join-Path $root 'src\VECLauncher.Installer\bin\Release\net48\VEC_Setup.exe'
    if (-not (Test-Path $setupExe)) { throw "VEC_Setup.exe not found at $setupExe" }

    $destSetup = Join-Path $outDir 'VEC_Setup.exe'
    Copy-Item $setupExe -Destination $destSetup -Force

    $webAppDownloads = Join-Path $root 'VyacheslavsMegaWebApp-dev\VyacheslavsMegaWebApp-dev\VyacheslavsMegaWebApp\wwwroot\downloads'
    if (Test-Path $webAppDownloads) {
        Copy-Item $destSetup (Join-Path $webAppDownloads 'VEC_Setup.exe') -Force -ErrorAction SilentlyContinue
    }
}

# ----------------------------------------------------
# SUMMARY
# ----------------------------------------------------
Write-Host ''
Write-Host '===================================================' -ForegroundColor Green
Write-Host ' BUILD SUCCESSFUL' -ForegroundColor Green
Write-Host '===================================================' -ForegroundColor Green

Get-ChildItem -Path $outDir | ForEach-Object {
    $fileMb = [math]::Round($_.Length / 1MB, 2)
    Write-Host ("  {0,-30} {1,8} MB" -f $_.Name, $fileMb) -ForegroundColor Yellow
}
Write-Host ''
Write-Host 'Artifacts created in dist/ folder ready for distribution.' -ForegroundColor DarkGray
Write-Host ''