# /goal instruction — Alko Olimpiada, next: playtest

> One prototype = one goal. When done, replace with the next one.
>
> **All 7 GDD competitions are in.** Done so far:
> - Hub-island (sand disc + water, fall = beach respawn), FP movement, NGO multiplayer.
> - Online via Unity Relay (room codes) + Vivox voice — both verified E2E. LAN fallback by IP.
> - Drunk system: 0–100 + permanent Floor from competition drinking, stages
>   (Szumi 20 / Lekko chycony 45 / Jest ligancko 70), sway + SoT-style veering, Zgon + revive [E],
>   voluntary vomit [V] (caught on camera within 25 m/40° = −2 pts, never below 0).
> - Beer in hand (visible bottle, max 1): [E] pick up, [F] drink, [G] discard. Special beers
>   (~25%, look normal, labelled): ×2 pts next competition + curse. Pills [Q]: stealth spike,
>   random effect, no message. Curses are a bitmask and stack: flip/lowres/zoom/tiny-view,
>   small/giant player, inverted controls, octodad (WSAD rerolls every 3 s).
> - Aggression: LMB push from stage 2, crosshair + hint.
> - Competitions (each own arena scene, medals 5/3/1, weighted-draw voting, champion at end):
>   Sprint na 500, Rzutki, Na pół, Lucky Shot (ladder), Spacer do monopolowego (free movement),
>   **Flanki** (timing wheel throw at the can → team chugs until opponent taps A-D-A-D),
>   **Beer Pong** (timing wheel, 6 cups, hit = opponents drink permanently).
> - Smoke flags: `-autohost/-autojoin/-autohostonline/-autojoincode X/-profile X/-autodrink`
>   + 7 per-competition auto flags; all 7 at once = full olympiad E2E.

---

## Goal

**Playtest with friends over the internet** (room code + voice). Collect feedback per competition
(is it funny? too long? too easy?), then tune the knobs below or add art/animations.

## Known knobs (balance)

- `DrunkSystem`: decay 0.2, beer +15, revive max(50, Floor), vomit drain 6/s, spiked +35,
  pill curse 40 s, stage thresholds in `Stages`, veer 30° max.
- `TeamCompetition` (Flanki/Pong): wheel 140°/s +120% at max drunk, green arc ±22°, turn 8 s.
- `Flanki`: sip 4 (−60% drunk), mug 100, reset 14 alternations. `BeerPong`: 6 cups, +6/cup.
- Others as before (Sprint sip 4, Rzutki wander 0.12, NaPol speed 0.25, Lucky 3+round arrows).

## Known limitations (accepted for prototype)

- Team split is even/odd index — no team picker. Odd player counts give uneven teams.
- Character scale curses don't scale the CharacterController collider (visual gag only).
- Timing-wheel hits are judged at server receive time — generous arc compensates lag.
- NaPol/level and LuckyShot input are client-trusted beyond basic clamps.

## Notes for next session

- Art pass: player models, bottle/can/cup props, island decor (GDD: agora, columns exist).
- Aggression during Flanki drinking phase? (pushing the drinker — GDD-adjacent fun).
- Poker (GDD 8.8) is the only unbuilt activity; needs card UI + betting — big.
