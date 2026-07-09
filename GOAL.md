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

**Playtest with friends**: 3+ players over LAN, full olympiad (voting → 3 competitions → champion),
collect feedback. Then decide: balance tuning vs. next competition vs. aggression mechanic.

## Voting (done, GDD section 4)

`VoteManager` (in-scene, Hub) + stations as vote spots: [R] near a station = vote for it
(toggle). All players voted → 3 s "LOSOWANIE..." → weighted draw (each vote = one ticket)
→ arena loads. Played competitions leave the pool (station shows ROZEGRANA). All played →
final summary + champion, [R] starts a new olympiad (resets scores and pool). `played` set
and `Olympics` scores are static — they survive scene switches, reset only via NewOlympicsRpc.

## Known knobs (balance)

- `DrunkSystem`: `decayPerSecond` 0.2 (slow sober-up), `beerStrength` 15, revive to max(50, Floor),
  `vomitDrainPerSecond` 6 ([V] voluntary vomit, hub only; caught within `catchRadius` 6 m →
  −`catchPenalty` 2 pts, once per vomit), `spikedExtra` 35 (pill in beer). `Floor` = permanent
  drunk from competitions (AddPermanent; capped 90) — decay/vomit can't go below it.
  Stages: Szumi 20 (sway), Lekko chycony 45 (A/D swap, pushing unlocked), Jest ligancko 70
  (WSAD → random hidden keys).
- Special beers (~25%, golden): drunk instantly on [E], ×2 points next competition + random
  curse during it (1 flipped screen / 2 renderScale 0.15 / 3 zoom FOV 20 / 4 tiny viewport).
- Pills: [E] pick up (4 on hub), [Q] near a bottle spikes it (server-only flag, invisible);
  victim discovers on drink (+35 drunk).
- Aggression: LMB push (stage ≥ 2, hub only, `pushForce` 8, cooldown 1 s).
- `Sprint500`: `sipBase` 4, `sipDrunkPenalty` 0.6, `drunkPerSip` 0.6 (permanent), vomit → max(60, Floor).
- `Rzutki`: `aimWander` 0.12, 3 darts, timeout 30 s; `NaPol`: `baseSpeed` 0.25, timeout 20 s;
  `LuckyShot`: 6 arrows, 3 s show, timeout 25 s; `Spacer`: free movement, fall → restart, timeout 60 s.
  All non-drinking competitions: `naturalDrunkGain` 12 (permanent).

## Known limitations (accepted for prototype)

- Disconnect while on an arena → restart the app to rejoin (menu camera is gone).
- Rzutki/NaPol trust client-sent aim/level (fine among friends).
- Scores keyed by clientId — reconnect starts a player at 0.

## Notes for next session

- Vivox still needs the Unity Cloud project linked in Project Settings → Services.
- Remaining competitions: Flanki and Beer Pong (both team-based, min 4 players, physics) —
  build after a 4+ player playtest confirms the core is fun.
- LuckyShot is a single memory round; GDD 8.7 wants a ladder/bracket — after playtest.
