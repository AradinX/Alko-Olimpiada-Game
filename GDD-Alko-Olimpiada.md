# Alko Olimpiada — Dokument Projektowy Gry (GDD)

> Wersja robocza 0.2 — pomysł + rozstrzygnięte pierwsze decyzje projektowe.
> Silnik: **Unreal Engine 5**. Platforma: **PC / Steam**. Perspektywa: **first-person**. Tryb: **multiplayer** (2–10 graczy), **wbudowany voice chat od startu**.

---

## 1. Pitch (jedno zdanie)

Imprezowa gra multiplayer w klimacie grecko-rzymskiej olimpiady, w której wcielasz się w greckiego „alkoholika-olimpijczyka" i rywalizujesz ze znajomymi w serii pijackich konkurencji o medale i tytuł mistrza.

## 2. Inspiracje i ton

- **Rozgrywka (gameplay):** gry typu *Pummel Party* / imprezowe zbiory mini-gier + luźne poruszanie się po mapie jak w *Gang Beasts* / *Fall Guys*.
- **Klimat par-gambling:** *Golf With Your Friends* / *gambling-friends* style — pierwszoosobowo, prosto, dużo śmiechu.
- **Ton:** komediowy, „pod bekę", groteskowy. Grecko-rzymski antyk zmieszany z współczesnymi rekwizytami (butelki po piwie, półmiski z kurczakiem). Kolorowo, zabawnie, przerysowanie.
- **Wizualnie:** prościej — proste postacie, czytelna stylistyka (bliżej pierwszej grupy inspiracji).
- **Mechanicznie:** prościej — nie potrzeba zaawansowanej symulacji (bliżej drugiej grupy inspiracji).

## 3. Filary rozgrywki (game pillars)

1. **Zabawa ze znajomymi** — priorytet to śmiech i wspólna gra, nie balans e-sportowy.
2. **Pijaństwo jako mechanika** — poziom upojenia zmienia sterowanie i percepcję (utrudnienie), ale daje też bonusy (dylemat: grać trzeźwo vs. korzystać z bonusów).
3. **Wolność na mapie między konkurencjami** — hub, po którym się chodzi, znajduje przedmioty i umawia na kolejną konkurencję.
4. **Rywalizacja o medale** — jasny cel: najwięcej złota = mistrz olimpiady.

---

## 4. Pętla rozgrywki (game loop)

```
LOBBY → wejście na MAPĘ (hub, wolne poruszanie)
        ↓
   gracze chodzą po mapie, zbierają piwa/papierosy/przekąski
        ↓
   gracze głosują na konkurencję z listy i klikają GOTOWY
        ↓
   losowanie ważone: im więcej głosów na konkurencję, tym większa
   szansa, że to ona będzie następna
        ↓
   ekran ładowania → KONKURENCJA (mini-gra)
        ↓
   wyniki: miejsca 1–3 dostają medale (punkty), reszta 0
        ↓
   powrót na MAPĘ (poziom upojenia rośnie)
        ↓
   powtórz aż wszystkie konkurencje rozegrane
        ↓
   PODSUMOWANIE → gracz z największą liczbą punktów wygrywa (tytuł)
```

**Punktacja medalowa:**
- 🥇 Złoto — 5 pkt
- 🥈 Srebro — 3 pkt
- 🥉 Brąz — 1 pkt
- pozostali — 0 pkt

Zwycięzca całej olimpiady: najwięcej punktów łącznie (przy remisie — patrz „Braki i pytania").

**Długość sesji:** olimpiada kończy się po rozegraniu **wszystkich konkurencji**.

**Wybór konkurencji:** gracze głosują z listy pozostałych konkurencji i klikają „Gotowy". Następna konkurencja wybierana jest **losowaniem ważonym głosami** — konkurencja z największą liczbą głosów ma proporcjonalnie największą szansę, ale nie ma gwarancji (element losowości i „beki" z przegranego głosowania).

---

## 5. Mapa / Hub (świat między konkurencjami)

- Otwarta przestrzeń w klimacie greckim (kolumny, agora, świątynie) z komediowymi, współczesnymi rekwizytami.
- **Wolne poruszanie się** (freedom of movement), swobodne chodzenie graczy.
- Rozstawione **przedmioty do zebrania**:
  - **Butelki piwa** — różne rodzaje, część **renderowana losowo** jako „specjalne", dają bonusy.
  - **Papierosy** — potęgują efekt upojenia na następną rundę, ale też dają bonusy (szczegóły do zaprojektowania).
  - **Przekąski / półmiski** (kurczaki itp.) — na razie głównie dekoracja klimatyczna (potencjał: redukcja upojenia? — patrz pytania).
- **Stanowiska konkurencji** rozmieszczone na mapie — gracze do nich podchodzą, **głosują z listy** i klikają „Gotowy" (mechanika losowania ważonego — patrz sekcja 4).

## 6. System upojenia (rdzeń gry)

Centralna mechanika. Skalowana wartość „poziom upojenia" na gracza.

**Źródła upojenia:**
- Picie podczas konkurencji (nieuniknione w wielu grach).
- Dobrowolne picie piw/szotów na mapie w zamian za bonusy.
- Papierosy — mnożnik/potęgowanie efektu na kolejną rundę.
- Naturalny przyrost: nawet grając „uczciwie" (bez bonusowych piw) z każdą kolejną konkurencją jesteś coraz bardziej wstawiony.

**Efekty upojenia (utrudnienia):**
- Obraz się „buja"/kołysze, powoli faluje.
- W grach opartych na timingu (kręcące się kółko) — kółko kręci się szybciej, obraz wiruje.
- W pokerze — rozmycie i „mieszanie" kart (patrz konkurencja dodatkowa).

**Efekty upojenia (fabularne/społeczne):**
- Postacie wraz z progresem stają się **bardziej agresywne** — na początku nie można popychać kolegów, z czasem można się „napierdzielać" (fizyczne zaczepki na mapie).

**HUD i Zgon (blackout):**
- Poziom upojenia widoczny jako **pionowy pasek po prawej stronie ekranu**.
- Gdy pasek dojdzie do końca **na hubie** — **ZGON**: gracz jest tak pijany, że nie może dalej grać (pada na ziemię, brak kontroli).
- Koledzy muszą go **ocucić** (mechanika cucenia do zaprojektowania: podejście + przytrzymanie klawisza? oblanie wodą? klepanie po twarzy?). Po ocuceniu poziom upojenia częściowo spada.
- Gdy pasek dojdzie do końca **podczas konkurencji** — **WYMIOTY** zamiast Zgonu: gracz na kilka sekund traci kontrolę (animacja rzygania), co kosztuje go czas/wynik w konkurencji, ale pasek upojenia spada. Konkurencja toczy się dalej — nikt nie odpada przez picie.

> **Dylemat projektowy (celowy):** bonusy za picie kuszą, ale każde piwo pogarsza sterowanie w kolejnych grach, a przegięcie kończy się Zgonem. Gracz balansuje ryzyko.

## 7. Postacie

- Proste, przerysowane postacie greckich „pijaków-olimpijczyków".
- Perspektywa first-person, ale postać ma model (widoczny dla innych w multiplayer).
- Progresja agresji zależna od poziomu upojenia (patrz wyżenj).

---

## 8. Konkurencje (mini-gry)

Docelowo **6 głównych** + 1 dodatkowa (poker). Z tego **2 drużynowe** (wymagają min. 4 graczy w lobby).

### 8.1. Sprint na 500 *(indywidualna)*
- Każdy ma kufel piwa. Kto najszybciej wypije, wygrywa.
- **Picie = szybkie klikanie SPACJI** — im szybciej klikasz, tym szybciej pijesz.
- Klasyka wyścigu na czas.

### 8.2. Flanki *(drużynowa, min. 4 graczy)*
- Lobby dzieli się na **2 drużyny** stojące naprzeciw siebie. Pośrodku boiska stoi **puszka**.
- Drużyny **na przemian rzucają kamieniem** w puszkę (rzut: obracające się kółko + trafienie SPACJĄ w odpowiednie pole).
- Gdy drużyna **trafi** w puszkę — jej gracze zaczynają pić piwo (klikanie SPACJI).
- Piją **do momentu**, aż drużyna przeciwna: podniesie puszkę ze środka, postawi ją z powrotem pionowo, a gracz-podnoszący wróci na swoją linię.
- **Do zaprojektowania:** akcja podnoszenia/stawiania puszki — pomysł: kombinacja/naprzemienne klikanie klawiszy (np. A-D-A-D lub sekwencja strzałek). *(Do dopracowania na dalszym etapie.)*

### 8.3. Beer Pong *(drużynowa)*
- Klasyczne zasady beer ponga. 2 drużyny, po ~10 kubków na stół.
- **Rzut = obracające się kółko**; trzeba kliknąć SPACJĘ w odpowiednim polu.
- Upojenie: kółko kręci się szybciej + obraz wiruje przy wyższym poziomie alkoholu.

### 8.4. Rzutki *(indywidualna)*
- Klasyczne rzutki, 3 rzuty na gracza. Najwięcej punktów = 1. miejsce.
- Mechanika celowania: obracające się kółko + SPACJA (lub inne urozmaicenie — do przemyślenia).
- Dogrywka między remisującymi (dodatkowy rzut / picie).

### 8.5. Na pół (Split the G) *(indywidualna)*
- Na wadze stoi kufel piwa. Trzeba wypić **dokładnie połowę**.
- Mechanika: **trzymanie SPACJI** i puszczenie, gdy myślisz, że to połowa.
- Wygrywa gracz najbliżej dokładnej połowy. Ranking wg odległości od 50%.

### 8.6. Spacer do monopolowego *(indywidualna, tor przeszkód)*
- Każdy gracz idzie z piwem w ręce przez mały tor przeszkód (kółka, ronda itp.).
- **Piwo się wylewa** — kontrola przez utrzymanie poziomu (np. trzymanie odpowiedniego położenia myszy / poziomu na ekranie).
- Gracz chodzi przód/tył. Piwo mierzone na starcie i na mecie.
- Wygrywa gracz z **największą ilością zachowanego piwa**.

### 8.7. Lucky Shot (Simon Says) *(indywidualna, tryb pucharowy/drabinka)*
- Losowanie par (dubel). Naprzeciw siebie stają 2 zawodników.
- Komunikaty **głosowe + tekstowe**: „Simon mówi kliknij SPACJĘ", „Simon mówi kliknij T"… a czasem samo „kliknij X" (bez „Simon mówi").
- Gracz reaguje tylko na komendy z „Simon mówi", chwyta kieliszek (SPACJA) i wypija.
- Kto się pomyli / nie wypije — **odpada** i przechodzi w **tryb widza** (ogląda dalsze pojedynki; bez mini-aktywności pobocznych — odchodzenie od konkurencji psuje vibe).
- Nieparzysta liczba graczy: gracz z „ostatniego miejsca" wchodzi jako trzeci — w finale np. 3 osoby, wygrywa ten, kto pierwszy poprawnie kliknie.

### 8.8. Poker (Texas Hold'em) *(hazard; rozważane jako zwykła konkurencja zamiast dodatkowej)*
- Klasyczny Texas Hold'em z twistem upojenia:
  - **Trzeźwy / lekko wstawiony:** karty widoczne normalnie (lekkie rozmycie).
  - **Mocniej wstawiony:** silne rozmycie — trudno rozróżnić kolor (kier/pik/trefl/karo), ale wartość (np. „4" / „5") bywa czytelna, lub odwrotnie.
  - **Bardzo pijany:** karty „mieszają się w oczach" — raz widzisz asa, raz króla, raz damę (tylko wizualnie; **realne karty są stałe**). To samo z kartami na stole.
- **Stawka:** obstawiamy liczbą **szotów**, które deklarujemy się wypić (szot < piwo w skali upojenia — do wyważenia). Maks. np. 5 szotów do dyspozycji.
- **Brak szotów = koniec gry** — gracz bez szotów nie może obstawiać i odpada od stołu (przechodzi w tryb widza).
- Gra realnie zwiększa poziom upojenia w miarę deklaracji.
- Wygrywa gracz, który zostaje do końca przy stole.

---

## 9. Tryb multiplayer

- **2–10 graczy** (docelowo skalowanie; niektóre konkurencje drużynowe wymagają min. 4).
- Model: jeden gracz **tworzy grę/lobby**, znajomi **dołączają**.
- First-person, wszyscy widzą się na wspólnej mapie.
- **Voice chat wbudowany od startu** (UE VOIP; docelowo Steam Voice) — gra imprezowa bez głosu traci połowę humoru.
- Gracze wyeliminowani w trakcie konkurencji (Lucky Shot, Poker) **oglądają jako widzowie** do końca konkurencji.
- **Do zaprojektowania (kluczowe technicznie):** architektura sieci (host/klient vs. dedykowany serwer), autorytet nad stanem gry, synchronizacja upojenia i wyników.

---

## 10. Uwagi techniczne (Unreal Engine)

- **Netcode:** UE ma wbudowaną replikację (Actor Replication, RPC). Najprościej na start: **listen server** (host jest jednym z graczy). Dedykowany serwer później, jeśli będzie potrzeba.
- **Struktura:** mapa-hub jako osobny Level; każda konkurencja jako **osobny Level/sublevel** ładowany po głosowaniu (ekran ładowania = Level streaming / open level).
- **Mechaniki input-timing** (kółko + SPACJA, klikanie, trzymanie) są proste — nadają się na Blueprints na prototyp, C++ jeśli będzie potrzeba wydajności.
- **Efekt upojenia:** post-process (rozmycie, chromatic aberration, falowanie kamery) sterowany jedną zmienną „drunkLevel" — łatwo skalowalny na wszystkie efekty.
- **Dostępność:** suwak intensywności efektów bujania/falowania w opcjach (choroba lokomocyjna w first-person to realne ryzyko) — od pierwszego prototypu.
- **Losowe „specjalne" butelki:** prosty spawner z tabelą przedmiotów/rzadkości.

---

## 11. Braki, ryzyka i pytania do rozstrzygnięcia

**Rozstrzygnięte (v0.2):** długość sesji (wszystkie konkurencje), wybór konkurencji (głosowanie + losowanie ważone), wyeliminowani = widzowie, voice chat wbudowany od startu, pasek upojenia + Zgon, UE5 + PC/Steam, poker: brak szotów = odpadasz.

Rzeczy, których pomysł jeszcze nie precyzuje — do decyzji przed produkcją:

1. **Remisy w klasyfikacji generalnej** — co przy równej liczbie punktów na koniec olimpiady? (np. więcej złotych medali > srebrnych; albo dogrywka).
2. **Balans upojenia** — skala liczbowa? Ile piwo/szot/papieros dodają? Czy jest sufit i czy da się wytrzeźwieć (jedzenie? czas?). To decyduje o „czuciu" całej gry.
3. **Bonusy z piw i papierosów** — trzeba je konkretnie zdefiniować (jakie, jak silne, czy nie psują balansu). Obecnie tylko ogólny pomysł.
4. **Mechaniki „do przemyślenia"** wskazane przez Ciebie: podnoszenie puszki we Flankach, urozmaicenie Rzutek. Wymagają domknięcia.
5. **Konkurencje drużynowe przy nieparzystej/małej liczbie graczy** — jak dobierać drużyny przy 5, 7, 9 graczach? Boty? Handicap?
6. **Motyw alkoholu vs. dystrybucja** — Steam/konsole mają wytyczne dot. treści alkoholowych i wieku (rating PEGI/ESRB). Warto uwzględnić od początku (możliwy „tryb bezalkoholowy"/reskin dla szerszej dystrybucji).
7. **Zakres na start (MVP)** — 6 konkurencji + poker + hub + netcode to dużo. Sugerowana kolejność: najpierw **1 konkurencja indywidualna (np. Sprint na 500) w multiplayerze + system upojenia**, dopiero potem reszta.
8. **Sterowanie oparte głównie o SPACJĘ** — kilka gier korzysta z tego samego schematu (klikanie/trzymanie SPACJI). Warto zróżnicować, żeby konkurencje nie zlewały się w odczuciu.
9. **Głos w Lucky Shot** — komunikaty głosowe = nagrania audio (lektor). Do budżetu/produkcji dźwięku.
10. **Mechanika cucenia przy Zgonie** — jak koledzy cucą pijanego (przytrzymanie klawisza? woda? klepanie?) i ile upojenia spada po ocuceniu i po wymiotach (te dwie wartości ustawiają balans ryzyka).
11. **Rozłączenia** — co gdy gracz (a zwłaszcza **host** na listen serverze) straci połączenie w trakcie konkurencji. Do rozstrzygnięcia przed budową netcode'u.
12. **Poker: dodatkowa czy zwykła konkurencja** — skłaniamy się ku zwykłej (medale jak reszta); do potwierdzenia po playteście.

---

## 12. Sugerowana ścieżka MVP (moja rekomendacja)

> Nie budujemy 7 gier naraz. Najpierw fundament, który udowodni, że pomysł jest fun.

1. **Prototyp 1:** hub-mapa + poruszanie się + multiplayer (2 graczy, listen server).
2. **Prototyp 2:** system upojenia (post-process + jedna zmienna) + zbieranie piw.
3. **Prototyp 3:** jedna konkurencja (Sprint na 500) z pełną pętlą: głosowanie → ładowanie → gra → medale → powrót.
4. **Test ze znajomymi** — czy jest śmiesznie? Dopiero potem dokładamy kolejne konkurencje.

---

*Dokument zbiera pierwotny pomysł. Konkurencje można z czasem rozszerzać. Kolejny krok: rozstrzygnąć pytania z sekcji 11 i doprecyzować balans upojenia.*
