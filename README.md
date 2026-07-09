# 🏛️ Alko Olimpiada

Imprezowa gra multiplayer (2–10 graczy) w klimacie grecko-rzymskiej olimpiady. Wcielasz się w greckiego „alkoholika-olimpijczyka" i rywalizujesz ze znajomymi w pijackich konkurencjach o medale i tytuł mistrza.

**Silnik:** Unreal Engine 5 · **Perspektywa:** first-person · **Tryb:** multiplayer (listen server)

## Rdzeń gry

- **Hub-mapa** w greckim klimacie — wolne poruszanie się między konkurencjami, zbieranie piw/papierosów/przekąsek.
- **System upojenia** — picie daje bonusy, ale pogarsza sterowanie i percepcję (bujanie obrazu, szybsze kółka timingowe, rozmyte karty w pokerze).
- **7 konkurencji** + poker: Sprint na 500, Flanki, Beer Pong, Rzutki, Na pół (Split the G), Spacer do monopolowego, Lucky Shot (Simon Says).
- **Punktacja:** 🥇 5 pkt · 🥈 3 pkt · 🥉 1 pkt. Najwięcej punktów = mistrz olimpiady.

Pełny opis: [GDD-Alko-Olimpiada.md](GDD-Alko-Olimpiada.md)

## Status

🚧 Prototyp — patrz [GOAL.md](GOAL.md) (aktualny cel) i sekcja 12 GDD (ścieżka MVP).

## Roadmapa MVP

1. Hub-mapa + poruszanie + multiplayer (2 graczy, listen server)
2. System upojenia (post-process, zmienna `DrunkLevel`) + zbieranie piw
3. Sprint na 500 z pełną pętlą: głosowanie → ładowanie → gra → medale → powrót
4. Playtest ze znajomymi → kolejne konkurencje

## Wymagania (dev)

- Unreal Engine 5.4+
- Git + Git LFS (`git lfs install` przed pierwszym klonem)

## Struktura projektu

```
AlkoOlimpiada/
├── AlkoOlimpiada.uproject
├── Config/
├── Content/
│   ├── Core/            # GameMode, GameState, PlayerState, GameInstance
│   ├── Characters/      # postać gracza (BP, mesh, animacje)
│   ├── Drunk/           # system upojenia (komponent, post-process materiały)
│   ├── Hub/             # level huba, pickupy (piwa, fajki), stanowiska konkurencji
│   ├── Minigames/
│   │   ├── Shared/      # wspólne mechaniki: kółko timingowe, licznik klikania SPACJI
│   │   ├── Sprint500/
│   │   ├── Flanki/
│   │   ├── BeerPong/
│   │   ├── Rzutki/
│   │   ├── NaPol/
│   │   ├── Monopolowy/
│   │   ├── LuckyShot/
│   │   └── Poker/
│   ├── UI/              # HUD, lobby, scoreboard, ekran wyników
│   └── Audio/
└── Source/              # C++ tylko jeśli Blueprints nie wystarczą
```
