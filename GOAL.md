# /goal instruction — Alko Olimpiada, next: playtest

> One prototype = one goal. When done, replace with the next one.
>
> Done so far:
> - **Prototype 1** — hub, FP movement, NGO multiplayer (host/join by IP), nametags, Vivox stub.
> - **Prototype 2** — drunk system (`DrunkSystem`, NetworkVariable 0–100), camera sway, Zgon + revive [E], beer pickups drunk with [F], HUD bar.
> - **Prototype 3** — Sprint na 500: station on hub, ready-up [R], countdown, SPACE-mash chug race (drunk handicap, chugging raises drunk, vomit instead of Zgon mid-race), medals 5/3/1, replicated scoreboard, back to hub. All in the Hub scene, no scene loading.
> - Smoke test flags: `-autohost` / `-autojoin` / `-autodrink` / `-autosprint`.

---

## Goal

**Playtest with friends** (roadmap step 4): 2+ players over LAN, run several Sprint na 500 rounds, collect feedback — is it funny? Then decide: tune balance vs. build the second competition (likely Rzutki or Lucky Shot per GDD section 8).

## Known knobs (balance)

- `DrunkSystem`: `decayPerSecond` 0.4, beer +15, revive to 50, sway from 15.
- `Sprint500`: `sipBase` 4, `sipDrunkPenalty` 0.6, `drunkPerSip` 0.6, vomit → 60, timeout 60 s.

## Notes for next session

- Vivox still needs the Unity Cloud project linked in Project Settings → Services.
- Voting UI becomes relevant once a second competition exists (weighted draw, GDD section 4).
- Aggression/pushing mechanic (GDD section 6) still unbuilt — candidate for a fun-injection later.
