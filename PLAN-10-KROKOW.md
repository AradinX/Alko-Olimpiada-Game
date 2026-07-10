# Plan rozwoju — 10 kroków w przód (2026-07-11)

> Analiza stanu: 7 konkurencji GDD gotowych mechanicznie, sieć (Relay+Vivox) działa E2E,
> system upojenia kompletny (etapy, klątwy, pigułki, Zgon). Greybox, IMGUI, zero art passu.
> Braki względem GDD: **papierosy** (sekcja 8/GDD pkt 3), **przekąski**, **poker/hazard**,
> art pass, post-process upojenia, ustawienia/dostępność, odporność na rozłączenia.

## Kroki

1. **Fix ConnectionUI** — duplikat na przeładowanym Hubie woła `AddNetworkPrefab` po starcie
   sieci (warning w logach) i subskrybuje `sceneLoaded` bez sprzątania. Guard + OnDestroy.
2. **Papierosy (GDD)** — pickup na hubie, [E] = zapalenie od razu: **+8 upojenia na stałe**
   (podłoga), w zamian „pewna ręka”: utrudnienia w NASTĘPNEJ konkurencji o połowę mniejsze
   (wobble Lucky Shot, kółko Flanek/Ponga, chlupanie Na pół, wander Rzutek, łyk Sprintu).
   Zużywa się po konkurencji jak klątwa z piwa specjalnego.
3. **OCZKO — hazard lite zamiast pokera (na razie)** — nowa konkurencja: klasyczne 21
   przeciw bankowi, 3 rozdania. Przed rozdaniem obstawiasz **w ciemno 1–3 szoty** — pijesz
   je natychmiast (upojenie!), wygrana płaci tyle żetonów, ile postawiłeś. Po pijaku karty
   „mieszają się w oczach” (GDD 8.8 twist). Ranking po żetonach. Drabinka ryzyka bez całej
   maszynerii pokera.
4. **Playtest online 2–4 graczy** — zwłaszcza Oczko i papierosy; notatki per konkurencja,
   strojenie gałek z GOAL.md (stawki szotów, siła papierosa, czasy faz).
5. **Przekąski (GDD)** — półmiski z kurczakiem na hubie: zjedzenie −10 upojenia (nie schodzi
   poniżej podłogi). Domyka trójkąt ryzyka: piwo daje bonusy, papieros pewną rękę, żarcie ratuje.
6. **Post-process upojenia (URP Volume)** — lens distortion + chromatic aberration + vignette
   sterowane poziomem upojenia; **suwak intensywności w opcjach** (choroba lokomocyjna — GDD 10).
7. **Menu ESC + ustawienia** — pauza lokalna: czułość myszy, głośność SFX/voice, suwak z kroku 6,
   „opuść grę”. IMGUI wystarczy.
8. **Art pass 1: postacie i propsy** — modele klockowych olimpijczyków (toga, wieniec),
   porządna butelka/kufel/kubek/tarcza, stoły. Zero Asset Store — proste meshe proceduralne
   albo CC0.
9. **Art pass 2: wyspa-agora** — kolumny, świątynia, plaża, ambient fal (Sfx loop), latarnie;
   czytelne strefy stanowisk zamiast kolorowych kwadratów.
10. **Poker (GDD 8.8) + odporność sieci** — pełny Texas Hold'em z twistem upojenia (duży —
    ostatni), plus: zachowanie przy rozłączeniu klienta w trakcie konkurencji, komunikat przy
    padzie hosta, ewentualnie lobby/reconnect.

## Zasada kolejności

Feel → zawartość → tuning → dopiero ładność. Kroki 1–3 robione w tej sesji; 4 wymaga ludzi;
5–7 to szybkie wygrane; 8–10 to duże klocki na koniec.
