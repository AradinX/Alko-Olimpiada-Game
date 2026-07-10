# 🏛️ Alko Olimpiada

Imprezowa gra multiplayer (2–10 graczy) w klimacie grecko-rzymskiej olimpiady. Wcielasz się w greckiego „alkoholika-olimpijczyka" i rywalizujesz ze znajomymi w pijackich konkurencjach o medale i tytuł mistrza.

**Silnik:** Unity 6 · **Perspektywa:** first-person · **Tryb:** multiplayer (host + klienci, Netcode for GameObjects)

## Rdzeń gry

- **Hub-mapa** w greckim klimacie — wolne poruszanie się między konkurencjami, zbieranie piw/papierosów/przekąsek.
- **System upojenia** — picie daje bonusy, ale pogarsza sterowanie i percepcję (bujanie obrazu, szybsze kółka timingowe, rozmyte karty w pokerze). Pełny pasek = Zgon (hub) lub wymioty (konkurencja).
- **8 konkurencji** + poker (w planach): Sprint na 500, Flanki, Beer Pong, Rzutki, Na pół (Split the G), Spacer do monopolowego, Lucky Shot (Simon Says), Oczko (21 ze stawkami w szotach).
- **Punktacja:** 🥇 5 pkt · 🥈 3 pkt · 🥉 1 pkt. Najwięcej punktów = mistrz olimpiady.

Pełny opis: [GDD-Alko-Olimpiada.md](GDD-Alko-Olimpiada.md)

## Status

🚧 Prototyp — patrz [GOAL.md](GOAL.md) (aktualny cel) i sekcja 12 GDD (ścieżka MVP).

## Roadmapa MVP

1. Hub-mapa + poruszanie + multiplayer (2 graczy, host + klient)
2. System upojenia (post-process, zmienna `drunkLevel`) + zbieranie piw
3. Sprint na 500 z pełną pętlą: głosowanie → ładowanie → gra → medale → powrót
4. Playtest ze znajomymi → kolejne konkurencje

## Stack techniczny

- **Unity 6** (URP)
- **Netcode for GameObjects** — multiplayer (host + dołączanie po IP)
- **Unity Vivox** — voice chat
- **Input System** (nowy) — sterowanie
- Git + Git LFS (`git lfs install` przed pierwszym klonem)

## Struktura projektu

```
AlkoOlimpiada/
├── Assets/
│   ├── Scenes/          # Hub + po jednej scenie na konkurencję
│   ├── Scripts/
│   │   ├── Core/        # GameManager, punktacja, głosowanie, sesja sieciowa
│   │   ├── Player/      # sterowanie FP, nickname, replikacja
│   │   ├── Drunk/       # system upojenia (drunkLevel, Zgon, wymioty)
│   │   ├── Pickups/     # piwa, papierosy, spawner
│   │   ├── Minigames/
│   │   │   ├── Shared/  # kółko timingowe, licznik klikania SPACJI
│   │   │   ├── Sprint500/
│   │   │   ├── Flanki/
│   │   │   ├── BeerPong/
│   │   │   ├── Rzutki/
│   │   │   ├── NaPol/
│   │   │   ├── Monopolowy/
│   │   │   ├── LuckyShot/
│   │   │   └── Poker/
│   │   └── UI/          # HUD (pasek upojenia), lobby, scoreboard
│   ├── Prefabs/
│   ├── Materials/
│   ├── Audio/
│   └── Settings/        # URP, Input Actions, Volume profiles
├── Packages/
└── ProjectSettings/
```
