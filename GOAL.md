# /goal instruction — Alko Olimpiada, Prototype 3

> Paste the block below as the session goal. One prototype = one goal. When done, replace with the next one (Prototype 4: playtest feedback / second minigame).
>
> Done so far: **Prototype 1** (hub, FP movement, NGO multiplayer, nametags, Vivox stub) · **Prototype 2** (drunk system: `DrunkSystem` NetworkVariable 0–100, camera sway, Zgon + revive with E, beer pickups on hub, HUD bar; smoke-tested via `-autohost`/`-autojoin`/`-autodrink`).

---

## Goal

Build **Prototype 3**: the first competition — **Sprint na 500** (chug race: mash SPACE to drink fastest) — with the full game loop: vote at a station on the hub → competition starts → play → medals awarded (5/3/1 pts) → back to the hub with increased drunk level.

## Context

- Full description: `GDD-Alko-Olimpiada.md` sections 4 (game loop), 8.1 (Sprint na 500), 6 (drunk effects during competitions — vomit instead of Zgon).
- Existing code: `Assets/Scripts/` (Player, Drunk, Pickups, Core). Editor scaffolding: `Assets/Editor/ProjectBootstrap.cs` (run methods via `-executeMethod` in batch mode; editor must be closed first).
- Smoke tests: build supports `-autohost` / `-autojoin` / `-autodrink` CLI flags.

## Scope (definition of done)

1. Competition station on the hub (greybox pillar/zone): walk up, press a key → "Ready" state; when all connected players are ready, the competition starts (voting list is overkill with one competition — YAGNI until there are two).
2. Sprint na 500 minigame: each player mashes SPACE to empty a beer mug (progress bar); higher drunk level = some handicap (per GDD: harder control). First to finish wins; ranking by finish time.
3. Results: medals 🥇5 / 🥈3 / 🥉1 pts, scoreboard replicated and visible to all; scores persist across rounds.
4. Return to hub after results; every participant's drunk level rises (natural progression per GDD section 6).
5. Vomit rule during competition: drunk bar full mid-game → few seconds of lost control, bar drops, game continues (no Zgon in competitions).

## Verification criteria

- Two instances (host + client): both ready up → minigame runs → medals awarded → both return to hub with updated scores and drunk levels.
- Headless smoke test flag (e.g. `-autosprint`) exercising the loop end to end.

## Constraints

- Same scene is fine (overlay/state switch); a separate scene only if it turns out simpler.
- No voting UI, no loading screen polish, no art. YAGNI.
