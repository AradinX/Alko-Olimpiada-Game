# Podsumowanie sesji 2026-07-11

Aktualizowane na bieżąco w trakcie pracy.

## Zrobione

- **PLAN-10-KROKOW.md** — analiza braków (papierosy, przekąski, hazard, art, post-process,
  ustawienia, sieć) i roadmapa na 10 kroków.
- **Commit zaległej zmiany** — kamera przypięta do głowy przy emotkach (leżenie/fikołek/taniec)
  z poprzedniej sesji.
- **Fix ConnectionUI** — powrót na Hub spawnuje duplikat komponentu, który zdążał wykonać
  `Start()` i rejestrował `beerPrefab` + callbacki na już działającej sieci (warning w logach).
  Guard `IsListening` + odsubskrybowanie `sceneLoaded` w `OnDestroy`.
- **Papierosy (GDD)** — `CigarettePickup` na hubie ([E] = zapalasz od razu, bez ekwipunku):
  **+8 upojenia na stałe** (podłoga), w zamian **„pewna ręka”** — utrudnienia z alkoholu
  w następnej konkurencji o połowę mniejsze. Jeden punkt wejścia: `DrunkSystem.Handicap01()`
  (Steady ⇒ ×0.5), używany przez `Competition.LocalDrunk01` (wobble Lucky Shot, chlupanie
  Na pół, wander Rzutek), `TeamCompetition.GetDrunk01` (prędkość kółka Flanki/Pong) i kary
  łyka w Sprincie/Flankach. Efekt zużywa się w `Competition.Finish` jak klątwa. Dźwięk "puff".
- **OCZKO — nowa konkurencja hazardowa** (prostsza od pokera, GDD 8.8 zostaje na później):
  klasyczne 21 przeciw bankowi, 3 rozdania. Stawka **1–3 szoty w ciemno** ([1][2][3]) —
  pijesz od razu (`AddCompetitionDrink`, stawka×5 upojenia; przegięcie = rzygasz jak w GDD).
  [SPACJA] dobierz / [S] stój, bank dobiera do 17, wygrana ±stawka żetonów, ranking po
  żetonach. Pijacki twist: karty **migoczą losowymi wartościami** tym częściej, im bardziej
  pijany (prawdziwe wartości stałe — jak w GDD pokerze). Flaga smoke: `-autooczko`.
- **Bootstrap `SetupPrototype7`** — prefab papierosa (4 szt. na hubie), stanowisko OCZKO
  (kasynowa zieleń, (0,-18)), scena `Arena_Oczko` (zielony stół, gracze w kręgu), wpis do
  VoteManagera i build settings. Idempotentny.

- **Smoke test Oczko PASSED** — 2 headless instancje LAN: głosowanie → 3 rozdania →
  medale (`#1 +5 pkt / #2 +3 pkt`). Rozliczenia poprawne (fura gracza przegrywa zawsze,
  fura banku płaci stojącym).
- **Przekąski (GDD, krok 5 planu)** — `SnackPickup`: półmisek z kurczakiem na hubie,
  [E] = jesz, upojenie −10 (nigdy poniżej podłogi). Ratunek bez ryzyka przyłapania jak
  przy rzyganiu, ale 3 sztuki i respawn 60 s. Trójkąt ryzyka domknięty:
  piwo = bonus, szlug = pewna ręka, kurczak = ratunek.
- **Menu ESC + ustawienia (krok 7 planu)** — `GameSettings` (PlayerPrefs) + `PauseMenu`
  na NetworkManagerze: czułość myszy, głośność SFX, **suwak bujania ekranu**
  (dostępność/choroba lokomocyjna — GDD 10). Wpięte w PlayerController, Sfx i DrunkSystem.

- **Post-process upojenia (krok 6 planu)** — LensDistortion (falująca soczewka) +
  ChromaticAberration + Vignette na kamerze właściciela; profil Volume budowany w kodzie
  (zero assetów w scenach), intensywność = upojenie × suwak bujania. **Wymaga wizualnej
  weryfikacji przy playteście** (headless smoke nie renderuje).
- **Drugi pełny smoke PASSED** — `-autodrink` (Zgon → cucenie → wyrzucenie piwa) +
  pełne Oczko z medalami w jednym przebiegu; sceny z przekąskami/papierosami/PauseMenu
  wczytują się czysto.

- **Commity**: fix ConnectionUI (osobno) + paczka feature'ów (papierosy, przekąski,
  OCZKO, menu ESC, post-process, bootstrap, sceny/prefaby, docs).
- **Stretch z GOAL.md** — `HubAmbience`: zapętlony szum fal generowany w kodzie (głośność
  faluje jak przybój, respektuje suwak głośności); pływające etykiety TMP nad stanowiskami
  konkurencji (`Billboard` obraca do kamery).

- **Pełna olimpiada E2E PASSED** — 2 instancje headless rozegrały wszystkie 8 konkurencji
  (Beer Pong → Sprint → Lucky Shot → Rzutki → Na pół → Flanki → Spacer → **Oczko**),
  `KONIEC OLIMPIADY — mistrz` wyłoniony, zero wyjątków w logach.

- **Finalny build PASSED** — exe z ambientem fal i etykietami startuje czysto (host smoke,
  zero wyjątków).

## Weryfikacja wizualna na playtest (headless nie renderuje)

- Post-process upojenia (falująca soczewka, aberracja, winieta) i jego suwak.
- Etykiety nad stanowiskami (rozmiar/wysokość), słyszalność fal, panel ESC.
- Migotanie kart w Oczku po pijaku i czytelność symboli ♠♥♦♣ w foncie IMGUI.

## Do zrobienia po sesji (patrz PLAN-10-KROKOW.md)

- Playtest online (Oczko + papierosy + kurczaki), post-process upojenia (URP Volume),
  art pass, poker, odporność na rozłączenia.
