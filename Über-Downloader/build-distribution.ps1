# ==============================================================================
# Script de génération du package de distribution publique — Über-Downloader
# ==============================================================================

param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

$rootDir = $PSScriptRoot

# 1. Résolution de la version
if ([string]::IsNullOrWhiteSpace($Version)) {
    $propsPath = Join-Path $rootDir "Directory.Build.props"
    if (Test-Path $propsPath) {
        [xml]$propsXml = Get-Content -Path $propsPath -Raw
        $Version = $propsXml.Project.PropertyGroup.Version
    }
    if ([string]::IsNullOrWhiteSpace($Version)) {
        $Version = "1.0.0"
    }
}

# Normalisation pour AssemblyVersion / FileVersion (ex: 1.0.0 -> 1.0.0.0)
$versionParts = $Version.Split('.')
while ($versionParts.Count -lt 4) {
    $versionParts += "0"
}
$fullAssemblyVersion = ($versionParts[0..3]) -join '.'

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "   Distribution Über-Downloader v$Version" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

# 2. Arrêt des processus en cours
Write-Host "[1/6] Arrêt des processus existants éventuels..." -ForegroundColor Yellow
Get-Process -Name "UberDownloader*" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

$distDir = Join-Path $rootDir "dist"
$win64Dir = Join-Path $distDir "win-x64"
$installerDir = Join-Path $distDir "installer"
$portableStagingDir = Join-Path $distDir "UberDownloader-v$Version-win-x64-portable"
$portableZipPath = Join-Path $distDir "UberDownloader-v$Version-win-x64-portable.zip"
$standaloneExePath = Join-Path $distDir "UberDownloader-v$Version-Standalone-win-x64.exe"
$shaSumsPath = Join-Path $distDir "SHA256SUMS.txt"

# Nettoyage préalable de dist/
if (Test-Path $distDir) {
    Write-Host "[2/6] Nettoyage du dossier dist/..." -ForegroundColor Yellow
    Remove-Item -Path $distDir -Recurse -Force
}

New-Item -ItemType Directory -Path $distDir -Force | Out-Null
New-Item -ItemType Directory -Path $win64Dir -Force | Out-Null

# 3. Compilation et publication Release self-contained
Write-Host "[3/6] Compilation & publication Release self-contained win-x64..." -ForegroundColor Cyan
$projectPath = Join-Path $rootDir "src\UberDownloader.Wpf\UberDownloader.Wpf.csproj"

dotnet publish $projectPath -c Release -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=true `
    -p:Version=$Version `
    -p:AssemblyVersion=$fullAssemblyVersion `
    -p:FileVersion=$fullAssemblyVersion `
    -o $win64Dir

if ($LASTEXITCODE -ne 0) {
    throw "Échec lors de 'dotnet publish' (code d'erreur $LASTEXITCODE)."
}

# Suppression des fichiers .pdb de débogage
Get-ChildItem -Path $win64Dir -Filter "*.pdb" | Remove-Item -Force

# Copie du binaire standalone directement dans dist avec nommage versionné clair
$publishedExe = Join-Path $win64Dir "UberDownloader.Wpf.exe"
if (Test-Path $publishedExe) {
    Copy-Item $publishedExe $standaloneExePath -Force
}

# 3.5 Approvisionnement des outils autonomes (yt-dlp, ffmpeg, ffprobe)
Write-Host "[3.5/6] Intégration des outils autonomes (yt-dlp, FFmpeg, FFprobe)..." -ForegroundColor Cyan
$toolsCacheDir = Join-Path $rootDir "tools_cache"
$binDir = Join-Path $win64Dir "bin"
New-Item -ItemType Directory -Path $toolsCacheDir -Force | Out-Null
New-Item -ItemType Directory -Path $binDir -Force | Out-Null

$ytdlpCache = Join-Path $toolsCacheDir "yt-dlp.exe"
if (-not (Test-Path $ytdlpCache) -or (Get-Item $ytdlpCache).Length -lt 5MB) {
    Write-Host "Téléchargement de yt-dlp.exe autonome..." -ForegroundColor Yellow
    Invoke-WebRequest -Uri "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe" -OutFile $ytdlpCache -UseBasicParsing
}
Copy-Item $ytdlpCache (Join-Path $binDir "yt-dlp.exe") -Force

$ffmpegCache = Join-Path $toolsCacheDir "ffmpeg.exe"
$ffprobeCache = Join-Path $toolsCacheDir "ffprobe.exe"
if (-not (Test-Path $ffmpegCache) -or -not (Test-Path $ffprobeCache) -or (Get-Item $ffmpegCache).Length -lt 5MB) {
    Write-Host "Téléchargement du pack FFmpeg autonome (yt-dlp FFmpeg-Builds)..." -ForegroundColor Yellow
    $ffmpegZip = Join-Path $toolsCacheDir "ffmpeg.zip"
    Invoke-WebRequest -Uri "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip" -OutFile $ffmpegZip -UseBasicParsing
    
    $tempExtract = Join-Path $toolsCacheDir "ffmpeg_extract"
    if (Test-Path $tempExtract) { Remove-Item $tempExtract -Recurse -Force }
    Expand-Archive -Path $ffmpegZip -DestinationPath $tempExtract -Force
    
    $extractedFfmpeg = Get-ChildItem -Path $tempExtract -Filter "ffmpeg.exe" -Recurse | Select-Object -First 1
    $extractedFfprobe = Get-ChildItem -Path $tempExtract -Filter "ffprobe.exe" -Recurse | Select-Object -First 1
    
    if ($extractedFfmpeg) { Copy-Item $extractedFfmpeg.FullName $ffmpegCache -Force }
    if ($extractedFfprobe) { Copy-Item $extractedFfprobe.FullName $ffprobeCache -Force }
    
    Remove-Item $tempExtract -Recurse -Force
    Remove-Item $ffmpegZip -Force
}
Copy-Item $ffmpegCache (Join-Path $binDir "ffmpeg.exe") -Force
Copy-Item $ffprobeCache (Join-Path $binDir "ffprobe.exe") -Force

# 4. Création du package Portable
Write-Host "[4/6] Préparation de l'archive portable ZIP..." -ForegroundColor Cyan
New-Item -ItemType Directory -Path $portableStagingDir -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $portableStagingDir "data") -Force | Out-Null

Copy-Item $publishedExe (Join-Path $portableStagingDir "UberDownloader.exe") -Force
Copy-Item (Join-Path $rootDir "LICENSE") $portableStagingDir -Force
Copy-Item (Join-Path $rootDir "THIRD-PARTY-NOTICES.txt") $portableStagingDir -Force
Copy-Item (Join-Path $rootDir "README.md") $portableStagingDir -Force
Copy-Item (Join-Path $rootDir "Uber.ico") $portableStagingDir -Force
Copy-Item $binDir $portableStagingDir -Recurse -Force

$scriptsSrc = Join-Path $win64Dir "Scripts"
if (Test-Path $scriptsSrc) {
    Copy-Item $scriptsSrc $portableStagingDir -Recurse -Force
}

$playwrightSrc = Join-Path $win64Dir ".playwright"
if (Test-Path $playwrightSrc) {
    Copy-Item $playwrightSrc $portableStagingDir -Recurse -Force
}

# Flag de mode portable
New-Item -ItemType File -Path (Join-Path $portableStagingDir "portable.flag") -Force | Out-Null

if (Test-Path $portableZipPath) { Remove-Item $portableZipPath -Force }
Compress-Archive -Path "$portableStagingDir\*" -DestinationPath $portableZipPath -CompressionLevel Optimal

# 5. Détection et compilation Inno Setup
Write-Host "[5/6] Recherche du compilateur Inno Setup..." -ForegroundColor Cyan
$isccPath = $null
$candidatePaths = @(
    "iscc.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)

foreach ($cand in $candidatePaths) {
    if ([string]::IsNullOrWhiteSpace($cand)) { continue }
    if (Get-Command $cand -ErrorAction SilentlyContinue) {
        $isccPath = (Get-Command $cand).Source
        break
    } elseif (Test-Path $cand) {
        $isccPath = $cand
        break
    }
}

if ($isccPath) {
    Write-Host "Compilation de l'installeur via $isccPath (Version: $Version)..." -ForegroundColor Green
    & $isccPath "/DMyAppVersion=$Version" (Join-Path $rootDir "installer.iss")
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Inno Setup a retourné une erreur (code $LASTEXITCODE)."
    }
} else {
    Write-Host "Inno Setup n'est pas détecté. Pour générer l'installeur : winget install JRSoftware.InnoSetup" -ForegroundColor Yellow
}

# 6. Calcul des empreintes de sécurité SHA-256
Write-Host "[6/6] Calcul des empreintes SHA-256..." -ForegroundColor Cyan
$hashes = @()

# Portable ZIP
if (Test-Path $portableZipPath) {
    $h = Get-FileHash -Path $portableZipPath -Algorithm SHA256
    $hashes += "$($h.Hash)  $([System.IO.Path]::GetFileName($h.Path))"
}

# Standalone EXE
if (Test-Path $standaloneExePath) {
    $h = Get-FileHash -Path $standaloneExePath -Algorithm SHA256
    $hashes += "$($h.Hash)  $([System.IO.Path]::GetFileName($h.Path))"
}

# Installer EXE
$installerPath = Join-Path $installerDir "UberDownloader-v$Version-Setup-x64.exe"
if (Test-Path $installerPath) {
    $h = Get-FileHash -Path $installerPath -Algorithm SHA256
    $hashes += "$($h.Hash)  $([System.IO.Path]::GetFileName($h.Path))"
}

$hashes | Out-File -FilePath $shaSumsPath -Encoding utf8

Write-Host "==================================================" -ForegroundColor Green
Write-Host "Publication terminée avec succès !" -ForegroundColor Green
Write-Host "Dossier de sortie : $distDir" -ForegroundColor White
Write-Host "Fichiers générés :" -ForegroundColor White
$hashes | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
Write-Host "==================================================" -ForegroundColor Green
