# Build Windows + spakuj + wrzuc na GitHub Release. Jedno polecenie.
# Uzycie:  .\scripts\release.ps1 v0.7 [-Notes "co nowego"]
param(
  [Parameter(Mandatory)][string]$Tag,
  [string]$Notes = "Build testowy na Windows. Rozpakuj CALY folder, odpal AlkoOlimpiada.exe."
)
$ErrorActionPreference = "Stop"

$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.3f1\Editor\Unity.exe"  # ponytail: staly editor tej maszyny
$root  = Split-Path $PSScriptRoot -Parent
$proj  = Join-Path $root "AlkoOlimpiada"
$win   = Join-Path $proj "Builds\Win"
$zip   = Join-Path $proj "Builds\AlkoOlimpiada-Win.zip"

# Otwarty edytor blokuje batch-build
if (Get-Process Unity -ErrorAction SilentlyContinue) {
  throw "Zamknij Unity Editor przed buildem (batch-mode nie wejdzie)."
}

Write-Host "==> Build (kilka minut)..." -ForegroundColor Cyan
Start-Process -Wait -FilePath $unity -ArgumentList @(
  "-batchmode","-nographics","-quit","-projectPath",$proj,
  "-executeMethod","ProjectBootstrap.Build","-logFile","$env:TEMP\alko_build.log"
)
if (-not (Test-Path "$win\AlkoOlimpiada.exe")) {
  Get-Content "$env:TEMP\alko_build.log" -Tail 20
  throw "Build nie wyprodukowal exe - log wyzej."
}

Write-Host "==> Pakowanie..." -ForegroundColor Cyan
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$win\*" -DestinationPath $zip
"{0:N1} MB" -f ((Get-Item $zip).Length/1MB)

Write-Host "==> Release $Tag..." -ForegroundColor Cyan
# gh pisze do stderr (np. "release not found") -> przy Stop PS 5.1 by przerwal; steruj przez exit code
$ErrorActionPreference = "Continue"
# tag istnieje -> podmien plik; nie istnieje -> stworz release
& gh release view $Tag *> $null
if ($LASTEXITCODE -eq 0) {
  & gh release upload $Tag $zip --clobber
} else {
  & gh release create $Tag $zip --title "Alko Olimpiada $Tag" --notes $Notes
}
if ($LASTEXITCODE -ne 0) { throw "gh release nie powiodlo sie." }
Write-Host "Gotowe: https://github.com/AradinX/Alko-Olimpiada-Game/releases/tag/$Tag" -ForegroundColor Green
