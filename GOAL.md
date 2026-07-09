# /goal instruction — Alko Olimpiada, next: playtest

> One prototype = one goal. When done, replace with the next one.
>
> Done so far:
> - **Prototype 1** — hub, FP movement, NGO multiplayer (host/join by IP), nametags, Vivox stub.
> - **Prototype 2** — drunk system (`DrunkSystem` 0–100), camera sway, Zgon + revive [E], HUD bar.
> - **Prototype 3/4** — beer inventory ([E] pick up, [F] drink), three competitions with full loop
>   and **scene switching** (Hub → arena → medals → Hub): Sprint na 500 (SPACE-mash chug,
>   drink-tilt camera, opponents visibly lean back), Rzutki (wandering crosshair, 3 darts,
>   server-side raycast), Na pół (hold SPACE, release at half). Shared base `Competition`,
>   persistent scores in static `Olympics`, stations on hub (`CompetitionStation`, [R] to ready).
> - Smoke flags: `-autohost` / `-autojoin` / `-autodrink` / `-autosprint` / `-autorzutki` / `-autonapol`.

---

## Goal

**Playtest with friends**: 3+ players over LAN, run all three competitions, collect feedback.
Then decide: balance tuning vs. voting system (weighted draw, GDD section 4) vs. next competition.

## Known knobs (balance)

- `DrunkSystem`: `decayPerSecond` 0.4, `beerStrength` 15, revive to 50. Stages (thresholds in
  static `Stages`): Szumi 20 (sway starts, deepens with level), Lekko chycony 45 (A/D swapped),
  Jest ligancko 70 (WSAD remapped to random hidden keys, rerolled on each stage entry).
- `Sprint500`: `sipBase` 4, `sipDrunkPenalty` 0.6, `drunkPerSip` 0.6, vomit → 60.
- `Rzutki`: `aimWander` 0.12, 3 darts, timeout 30 s, `naturalDrunkGain` 12.
- `NaPol`: `baseSpeed` 0.25, `drunkSpeedBonus` 1.5, timeout 20 s, `naturalDrunkGain` 12.

## Known limitations (accepted for prototype)

- Disconnect while on an arena → restart the app to rejoin (menu camera is gone).
- Rzutki/NaPol trust client-sent aim/level (fine among friends).
- Scores keyed by clientId — reconnect starts a player at 0.

## Notes for next session

- Vivox still needs the Unity Cloud project linked in Project Settings → Services.
- Aggression/pushing mechanic (GDD section 6) unbuilt — candidate for fun-injection.
