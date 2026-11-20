param(
    [string]$Version = ""
)

$target = Get-ChildItem -Path $PSScriptRoot -Filter "build-distribution.ps1" -Recurse | Where-Object { $_.FullName -ne $MyInvocation.MyCommand.Path } | Select-Object -First 1

if ($target) {
    Set-Location $target.Directory.FullName
    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        & $target.FullName -Version $Version
    } else {
        & $target.FullName
    }
} else {
    throw "Script de distribution build-distribution.ps1 introuvable."
}
