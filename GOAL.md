# /goal instruction — Alko Olimpiada, next: feel & feedback (Prototype 6)

> One prototype = one goal. When done, replace with the next one.
>
> **All 7 GDD competitions are in and mechanically final for prototype.** Done so far:
> - Hub-island (sand disc + water, fall = beach respawn), FP movement, NGO multiplayer.
> - Online via Unity Relay (room codes) + Vivox voice — both verified E2E. LAN fallback by IP.
> - Drunk system: 0–100 + permanent Floor, stages Szumi 20 / Lekko chycony 45 /
>   Jest ligancko 90 (beer +12 → 2/4/8 beers, 9 = Zgon), sway + veering, Zgon + revive [E],
>   voluntary vomit [V] (caught within 25 m/40° = −2 pts), beer in hand [E]/[F]/[G],
>   special beers (×2 pts + curse), pills [Q], stacking curse bitmask, LMB push from stage 2.
> - Competitions (own arena scenes, medals 5/3/1, weighted-draw voting, champion at end):
>   Sprint na 500, Rzutki, Na pół, Spacer do monopolowego,
>   Lucky Shot (6-arrow sequence, 3-2-1-SHOT countdown, shot-glass drink anim,
>   arrows wobble when drunk),
>   Flanki (mouse-aim at can AND timing wheel, bottle-flight FX, can tips over, mug bar),
>   Beer Pong (mouse aim + hold-SPACE power, parabola sim, mandatory table bounce,
>   15-cup pyramid 5-4-3-2-1 with touching rims, ball-into-cup animation).
> - Hardening: WASD-dead-after-alt-tab fixed (keyboard reset on focus), host-frozen-after-
>   competition fixed (InputLocked computed from Current, not a flag).
> - Smoke flags: `-autohost/-autojoin/-autohostonline/-autojoincode X/-profile X/-autodrink`
>   + 8 per-competition auto flags; all 8 at once = full olympiad E2E.
> - (2026-07-11) Papierosy: [E] na hubie = +8 upojenia NA STAŁE, w zamian "pewna ręka" —
>   utrudnienia z alkoholu w następnej konkurencji ×0.5 (DrunkSystem.Handicap01, zużywa
>   się w Competition.Finish jak klątwa).
> - (2026-07-11) OCZKO (hazard lite, 8. konkurencja): 21 vs bank, 3 rozdania, stawka
>   1-3 szoty W CIEMNO (pijesz od razu), żetony = ranking, pijackie migotanie kart.
>   Flaga `-autooczko`.

---

## Goal

Make it FEEL like a party game, then playtest online. Audio + on-screen feedback carry
drunk comedy better than greybox visuals ever will — do this BEFORE the art pass.

## Scope (definition of done)

1. **Audio one-shots, zero external assets** — synthesize simple clips at runtime
   (`AudioClip.Create`: blips, plops, white-noise bursts) or a single CC0 WAV pack in
   `Assets/Audio`. Cover: throw, table bounce, ball-in-cup plop, bottle-hits-can clank,
   sip/chug, vomit, Zgon thud, revive slap, countdown beep + go, medal fanfare, SHOT.
   One `Sfx` static helper (PlayClipAtPoint) — no audio manager class. (ponytail: YAGNI)
2. **Hit/miss feedback**: short screen flash on own hit (green) / miss (grey) in the three
   throw games; results screen already exists — add medal colors (gold/silver/bronze text).
3. ~~Flanki aggression~~ — CUT by user decision (2026-07-10): no pushing during drinking.
4. **Online playtest with friends** (room code + Vivox), 2–4 players. Collect per-competition
   notes: funny? too long? too easy? Then tune the knobs below.
5. Stretch only if trivial: hub ambience loop (waves), floating station labels.

## Verification

- All 7 auto-flag smoke tests still pass E2E (full olympiad run).
- Manual: sounds audible in hub + the three throw games;
  playtest happened and knob changes are committed with a note which feedback drove them.

## Constraints

- No Asset Store packs, no audio middleware. No art pass yet (models/props are the NEXT
  prototype). No Poker (GDD 8.8 — big, last).

## Known knobs (balance — tune after playtest)

- `DrunkSystem`: decay 0.2, beer +12, revive max(50, Floor), vomit drain 6/s, spiked +35,
  pill curse 40 s, stage thresholds in `Stages`, veer 30° max.
- `TeamCompetition`: wheel 140°/s +120% at max drunk, green arc ±22° (Flanki only),
  turn 8 s (Pong 12 s).
- `Flanki`: sip 4 (−60% drunk), mug 100, reset 14 alternations, canAimRadius 0.4.
- `BeerPong`: 15 cups/team (pyramid, rims touch, ⌀0.2), +2.5/cup, speed 4.5–11 m/s,
  charge 1.1 s, bounce damping 0.6, catch radius 0.11, timeout 300 s.
- `LuckyShot`: 6 arrows, show 2.5 s, countdown 3 s, answer 8 s, wobble 70 px at max drunk.
- Others: Sprint sip 4, Rzutki wander 0.12, NaPol speed 0.25.
- `Oczko`: 3 rozdania, stawka 8 s / gra 20 s / rozliczenie 6 s, szot = 5 upojenia,
  bank dobiera do 17, migotanie kart = drunk01 × 0.9.
- `CigarettePickup`: koszt 8 upojenia (permanentne), respawn 45 s, pewna ręka = ×0.5.

## Known limitations (accepted for prototype)

- Team split is even/odd index — no team picker. Odd player counts give uneven teams.
- Character scale curses don't scale the CharacterController collider (visual gag only).
- Flanki wheel hit judged at server receive time — generous arc compensates lag.
- Pong trajectory computed from client-sent origin/dir/power (origin sanity-checked only).
- NaPol/LuckyShot input client-trusted beyond basic clamps.
- Lucky Shot drink animation is local-only (others don't see your glass move).
- Pong: next turn starts while the previous ball is still mid-flight (cosmetic).

## After this prototype

1. **Art pass**: player models, bottle/can/cup props, island decor (GDD: agora).
2. **Poker (GDD 8.8)** — the only unbuilt activity; card UI + betting, big.
