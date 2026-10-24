# Script de lancement propre de Uber-Downloader
Get-Process -Name "UberDownloader*" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Host "Compilation de la solution..." -ForegroundColor Cyan
$buildResult = Start-Process -FilePath "dotnet" -ArgumentList "build", "-c", "Debug" -NoNewWindow -Wait -PassThru

if ($buildResult.ExitCode -eq 0) {
    Write-Host "Compilation reussie ! Demarrage de l'application..." -ForegroundColor Green
    Start-Process -FilePath "src\UberDownloader.Wpf\bin\Debug\net10.0-windows\UberDownloader.Wpf.exe"
} else {
    Write-Host "Echec de la compilation." -ForegroundColor Red
}
