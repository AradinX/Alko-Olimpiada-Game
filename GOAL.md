# /goal instruction — Alko Olimpiada, next: playtest

> One prototype = one goal. When done, replace with the next one.
>
> **All 7 GDD competitions are in.** Done so far:
> - Hub-island (sand disc + water, fall = beach respawn), FP movement, NGO multiplayer.
> - Online via Unity Relay (room codes) + Vivox voice — both verified E2E. LAN fallback by IP.
> - Drunk system: 0–100 + permanent Floor from competition drinking, stages
>   (Szumi 20 / Lekko chycony 45 / Jest ligancko 90; beer +12 → 2/4/8 beers, 9 = Zgon),
>   sway + SoT-style veering, Zgon + revive [E],
>   voluntary vomit [V] (caught on camera within 25 m/40° = −2 pts, never below 0).
> - Beer in hand (visible bottle, max 1): [E] pick up, [F] drink, [G] discard. Special beers
>   (~25%, look normal, labelled): ×2 pts next competition + curse. Pills [Q]: stealth spike,
>   random effect, no message. Curses are a bitmask and stack: flip/lowres/zoom/tiny-view,
>   small/giant player, inverted controls, octodad (WSAD rerolls every 3 s).
> - Aggression: LMB push from stage 2, crosshair + hint.
> - Competitions (each own arena scene, medals 5/3/1, weighted-draw voting, champion at end):
>   Sprint na 500, Rzutki, Na pół, Lucky Shot (6-arrow sequence, 3-2-1-SHOT countdown,
>   shot-glass grab + head-tilt drink anim, arrows wobble when drunk),
>   Spacer do monopolowego (free movement),
>   **Flanki** (mouse-aim at the can AND timing wheel; bottle-flight FX, can tips over,
>   mug-level bar → team chugs until opponent taps A-D-A-D),
>   **Beer Pong** (mouse aim + hold-SPACE power, stepped-parabola sim, ball MUST bounce
>   off the table into a cup; per-cup bitmask, visible ball flight; hit = opponents drink).
> - Smoke flags: `-autohost/-autojoin/-autohostonline/-autojoincode X/-profile X/-autodrink`
>   + 7 per-competition auto flags; all 7 at once = full olympiad E2E.

---

## Goal

**Playtest with friends over the internet** (room code + voice). Collect feedback per competition
(is it funny? too long? too easy?), then tune the knobs below or add art/animations.

## Known knobs (balance)

- `DrunkSystem`: decay 0.2, beer +12, revive max(50, Floor), vomit drain 6/s, spiked +35,
  pill curse 40 s, stage thresholds in `Stages`, veer 30° max.
- `TeamCompetition`: wheel 140°/s +120% at max drunk, green arc ±22° (Flanki only now),
  turn 8 s (Pong 12 s).
- `Flanki`: sip 4 (−60% drunk), mug 100, reset 14 alternations, canAimRadius 0.4.
- `BeerPong`: 15 cups per team (5-4-3-2-1 pyramid from player's end, rims touching, ⌀0.2),
  +2.5/cup, throw speed 4.5–11 m/s, charge 1.1 s, bounce damping 0.6, catch radius 0.11.
- Others as before (Sprint sip 4, Rzutki wander 0.12, NaPol speed 0.25,
  Lucky: 6 arrows, show 2.5 s, countdown 3 s, answer 8 s).

## Known limitations (accepted for prototype)

- Team split is even/odd index — no team picker. Odd player counts give uneven teams.
- Character scale curses don't scale the CharacterController collider (visual gag only).
- Timing-wheel hits are judged at server receive time — generous arc compensates lag.
- NaPol/level and LuckyShot input are client-trusted beyond basic clamps.

## Plan — next steps (2026-07-10)

1. **Playtest online** (unchanged main goal): room code + Vivox, collect per-competition
   feedback. New throw mechanics (Pong/Flanki) and Lucky countdown need real-human tuning:
   pong charge time / speeds, flanki canAimRadius, lucky show/answer times.
2. **Feel/FX quick wins**: throw release sound-less now — add simple audio (throw, cup hit,
   sip, vomit, zgon) via AudioSource one-shots; hit/miss screen flash.
3. **Art pass**: player models, bottle/can/cup props, island decor (GDD: agora, columns exist).
4. **Aggression during Flanki drinking phase?** (pushing the drinker — GDD-adjacent fun).
5. **Poker (GDD 8.8)** is the only unbuilt activity; needs card UI + betting — big, last.

## Fixed this session

- Drunk thresholds moved: Jest ligancko 70→90 (beer 15→12): 2/4/8 beers per stage, 9 = Zgon.
- WASD dead after alt-tab: known Input System focus bug — keyboard device reset
  in `PlayerController.OnApplicationFocus`.
- Host frozen after a competition ended: `InputLocked` was a static flag cleared in
  `OnNetworkDespawn`, which can be skipped/overwritten on the host during scene switch.
  Now a static property computed from `Current` (destroyed object == null ⇒ unlocked).
