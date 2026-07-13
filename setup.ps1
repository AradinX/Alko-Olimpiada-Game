# Alko-Olimpiada - automatyczny setup srodowiska (Windows 10/11)
# Uruchom w PowerShell:  powershell -ExecutionPolicy Bypass -File setup.ps1
# Jesli ktorys krok padnie - zrob go recznie wg SETUP.md.

$ErrorActionPreference = "Stop"

Write-Host "== 1/5 Git (zawiera Git Bash i Git LFS) ==" -ForegroundColor Cyan
winget install --id Git.Git -e --accept-source-agreements --accept-package-agreements

Write-Host "== 2/5 Unity Hub ==" -ForegroundColor Cyan
winget install --id Unity.UnityHub -e --accept-package-agreements

# swiezo zainstalowane programy nie sa jeszcze w PATH tej sesji
$git = "C:\Program Files\Git\cmd\git.exe"
$hub = "C:\Program Files\Unity Hub\Unity Hub.exe"

Write-Host "== 3/5 Konfiguracja Git + LFS ==" -ForegroundColor Cyan
$name  = Read-Host "Podaj swoje imie/nick do gita"
$email = Read-Host "Podaj swoj email (ten z konta GitHub)"
& $git config --global user.name  $name
& $git config --global user.email $email
& $git lfs install

Write-Host "== 4/5 Klonowanie repozytorium ==" -ForegroundColor Cyan
$dest = "$env:USERPROFILE\Alko-Olimpiada-Game"
if (Test-Path $dest) {
    Write-Host "Folder $dest juz istnieje - pomijam klonowanie."
} else {
    # przy pierwszym kontakcie z GitHubem wyskoczy okno logowania przez przegladarke
    & $git clone https://github.com/AradinX/Alko-Olimpiada-Game.git $dest
}

Write-Host "== 5/5 Unity Editor 6000.5.3f1 (to potrwa, ~7 GB) ==" -ForegroundColor Cyan
& $hub -- --headless install --version 6000.5.3f1 --changeset c2eb47b3a2a9 -m windows-il2cpp | Out-Null

Write-Host ""
Write-Host "GOTOWE. Co dalej:" -ForegroundColor Green
Write-Host "1. Otworz Unity Hub i zaloguj sie (darmowe konto Unity Personal)."
Write-Host "2. Projects -> Add -> wskaz: $dest\AlkoOlimpiada  (podfolder, nie glowny katalog!)"
Write-Host "3. Pierwsze otwarcie projektu potrwa kilka minut."
Write-Host "4. W Unity: Edit -> Preferences -> External Tools -> ustaw Visual Studio."
Write-Host "Codzienna praca i zasady: SETUP.md w repo."
