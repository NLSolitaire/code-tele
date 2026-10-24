@echo off
setlocal
chcp 65001 >nul
title Über-Downloader

echo ==================================================
echo               Über-Downloader
echo ==================================================
echo ⏳ Mise à jour et vérification du code...

cd /d "%~dp0\Über-Downloader"
dotnet build UberDownloader.slnx -c Debug --nologo -v q
if errorlevel 1 (
    echo.
    echo ❌ Échec de la compilation.
    echo.
    pause
    exit /b 1
)

echo ✓ Compilation réussie. Démarrage de l'application...
start "" "src\UberDownloader.Wpf\bin\Debug\net10.0-windows\UberDownloader.Wpf.exe" %*
exit /b 0
