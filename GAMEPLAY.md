# ALKO-OLIMPIADA — stan gameplayu

Aktualny opis mechanik w grze (stan: 2026-07-12). Wartości liczbowe = domyślne
z prefabów/scen; większość da się stroić w Inspectorze bez dotykania kodu.

## Pętla gry

1. **Hub** (wyspa) — chodzisz, pijesz, palisz, jesz, emotki, popychanie.
2. Przy świątyni głosuje się na konkurencję (**[T]** przy stanowisku) — po 10 s
   od pierwszego głosu losowanie ważone głosami.
3. Konkurencja: teleport na arenę → odliczanie → gra → medale (5/3/2/1 pkt,
   punkty ×2 z piwa specjalnego) → powrót na hub.
4. Po **8 konkurencjach** — KONIEC OLIMPIADY, mistrz = najwięcej punktów.

Multiplayer: LAN (host+join po IP) albo online przez Unity Relay (kod pokoju,
max 10 graczy). Czat głosowy Vivox. Emotki **[1-6]**: machanie, leżenie,
fikołek, salut, taniec, wskazanie.

## Upojenie (pasek 0-100)

- Piwo z huba: **+12 pkt**. Trzeźwiejesz **0.2 pkt/s** (powoli!).
- Etapy i kary:
  | Próg | Etap | Efekt |
  |---|---|---|
  | 20 | Szumi | bujanie kamery, post-process (rozmycie/aberracje) rośnie z poziomem |
  | 45 | Lekko chycony | **[A] i [D] zamienione** (komunikat na ekranie) |
  | 64 | Jest ligancko | **WASD losowo pomieszane** (ukryte!) + losowy gag ekranowy |
  | 100 | **ZGON** | leżysz, czekasz aż ktoś ocuci **[E]** (wracasz na 50) |
- Pijacki zygzak: ruch znosi na boki tym mocniej, im więcej promili.
- W konkurencjach pełny pasek = **wymioty** (spadek do 60), nie Zgon.
- **Podłoga paska** (ciemnoczerwona): alkohol wypity w konkurencjach zostaje
  na stałe w **50%** — tego nie wytrzeźwiejesz ani nie wyrzygasz.
- Piwo za udział w konkurencji (naturalDrunkGain, zwykle 12) wchodzi
  **na starcie** konkurencji — utrudnienia czujesz w trakcie gry.

## Rzyganie [V]

Trzymasz **[V]** — rzygasz i trzeźwiejesz 6 pkt/s (do podłogi). Ryzyko:
jak inny gracz ma cię na widoku (25 m, w kadrze, bez przeszkód) —
**PRZYŁAPANY: -2 pkt olimpiady** (raz na rzyganie).

## Przedmioty na hubie

| Co | Klawisz | Efekt |
|---|---|---|
| **Piwo** | [E] podnieś, [F] pij, [G] wyrzuć | +12 upojenia; max 1 w ręce (widać butelkę w dłoni) |
| **Piwo SPECJALNE** (2 na przerwę) | jak wyżej | losuje: **x2, Spartańskie, Nike, Nemezis, Tarcza Ateny albo Tyche** |
| **Pigułka** (2 na przerwę) | [E] podnieś, **[Q] dosyp do piwa** | ofiara nic nie widzi; efekt: mocny kop (+35) albo klątwa ekranu na 40 s |
| **Papieros** (2 na przerwę) | [E] zapal | **pewna ręka**: utrudnienia z alkoholu ×0.5 w następnej konkurencji; koszt: +upojenie NA STAŁE |
| **Kurczak** (przekąska) | [E] zjedz | trzeźwiejesz do podłogi |

Klątwy ekranu (kumulują się): obraz do góry nogami, lowres, zoom, mały obraz,
postać mała/wielka, odwrócone sterowanie, octodad (WASD losuje się co 3 s).

## Popychanie

**PPM** (na hubie) — pchnięcie: ofiara leży 1.2 s, wstaje 0.6-2.6 s
(dłużej im bardziej pijana). Cooldown 1 s, zasięg 2.2 m.

## Konkurencje (8)

### SPRINT NA 500
Każdy ma stolik z kuflem. **[E]** bierzesz kufel, **SPACJA** łyk, **[E]**
odkładasz. **Nie widzisz ile zostało** — oceniasz po przechyle głowy (swojej
i rywali). Liczy się odłożenie **pustego** kufla; odłożysz z piwem = strata
czasu. Im bardziej pijany, tym mniejsze łyki (papieros łagodzi). Pełny pasek
= rzygasz 4 s. Chlanie tu zostaje na stałe (podłoga).

### RZUTKI
3 rzuty do własnej tarczy. Celujesz **myszką** (celownik pływa po alkoholu),
trzymasz **SPACJĘ** — kółko kurczy się i rośnie — puszczasz przy **małym**
kółku = mały rozrzut. Środek tarczy 10 pkt, dalej mniej.

### NA PÓŁ
3 rundy: **PIWO → WINO (1.5× szybciej) → WÓDKA (2.2×)**. Trzymaj **SPACJĘ**
(pijesz), puść dokładnie **na połowie** — bez znacznika, na oko. Po alkoholu
poziom leci szybciej i nierówno. Wygrywa najmniejsza suma odchyleń.

### LUCKY SHOT — "MENEL MÓWI"
Wszyscy wokół stołu z kieliszkiem. Komendy na ekranie, ~2 s na reakcję:
- „MENEL MÓWI: kliknij [K]" → **klikasz** (inaczej odpadasz),
- „Kliknij [K]" (bez prefiksu) → **pułapka**: klikniesz = odpadasz,
- „MENEL MÓWI: NIE klikaj [SPACJA]" → nie klikaj!
Po 6-9 komendach: „MENEL MÓWI: SHOT! **[F]**" — wygrywa najszybszy refleks.
Po alkoholu tekst komend pływa po ekranie.

### SPACER DO MONOPOLOWEGO
Tor przeszkód (fall guys): wąskie belki nad wodą + **spadające puszki**
+ **wirujące młoty** spychające z trasy. Spadniesz = od startu. Jedyna
konkurencja z wolnym ruchem — bujanie i pomieszane klawisze robią robotę.

### FLANKI (drużynowa)
Dwie drużyny naprzemiennie rzucają butelką w puszkę: celujesz **myszką**
(bez celownika!), trzymasz **SPACJĘ** — pasek siły pływa — puszczasz z siłą
dopasowaną do odległości (tolerancja ±1.3 m). Trafienie = twoja drużyna
**chleje** (SPACJA), aż przeciwnik postawi puszkę (**A-D-A-D** ×14).
Wygrywa drużyna, która pierwsza dopije kufle (100 na głowę).

### BEER PONG (drużynowa)
Klasyk: rzuty piłeczką w kubki przeciwnika (kółko timingowe + celowanie).
Trafienie = przeciwnik pije. Wygrywa zbicie wszystkich kubków.

### OCZKO (hazard lite)
Karty przy stole: dobierasz do 21. Stawki w szotach — przegrana = pijesz
w ciemno. Blef i szczęście.

## Punktacja olimpiady

Medale za konkurencję: **5 / 3 / 2 / 1** pkt (1. / 2. / 3. / reszta).
Specjalne piwo daje jeden efekt: x2; Spartańskie (x3 tylko za 1. miejsce, inaczej 0);
Nike (+2 za podium); Nemezis (kradnie 2 pkt liderowi, jeśli go pokonasz);
Tarcza Ateny (blokuje klątwę albo wymioty); Tyche (losuje -1..+4 pkt).
Kara za przyłapane rzyganie: -2 pkt. Tablica wyników jest stale widoczna.

**Zakłady:** w hubie [Tab] przełącza typowanego zwycięzcę. Stawka 1 pkt;
trafiony zakład wypłaca 2 pkt. Po pierwszej konkurencji, bo wcześniej nie masz punktów.

## Sterowanie — ściąga

| Klawisz | Akcja |
|---|---|
| WASD / Shift / Spacja | ruch / sprint / skok |
| E | podnieś / ocuć / weź kufel |
| F | wypij piwo z ręki / SHOT w Menel mówi |
| G | wyrzuć piwo |
| Q | dosyp pigułkę do piwa |
| V (trzymaj) | rzygaj (trzeźwiejesz, byle nikt nie widział) |
| T | głosuj przy stanowisku |
| PPM | pchnięcie |
| 1-6 | emotki |
| Tab | zmień/anuluj zakład na zwycięzcę następnej konkurencji |
| Esc | menu (czułość, suwak bujania, wyjście) |
