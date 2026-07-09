# /goal instruction — Alko Olimpiada, Prototype 1

> Paste the block below as the session goal. One prototype = one goal. When done, replace with the next one (Prototype 2: drunk system).

---

## Goal

Build **Prototype 1** of Alko Olimpiada in Unreal Engine 5: a hub map with first-person movement and working multiplayer on a listen server for 2 players.

## Context

- Full game description: `GDD-Alko-Olimpiada.md` (read sections 4, 5, 9, 10, 12 before starting; the document is in Polish — translate as needed).
- Everything in Blueprints — C++ only when Blueprints are not enough.
- Do not build anything beyond the scope below (no minigames, no drunk system, no pickups — those are Prototypes 2–3).

## Scope (definition of done)

1. UE5 project `AlkoOlimpiada` with the `Content/` folder structure described in `README.md`.
2. `Hub` level — greybox only: flat area, a few placeholder shapes for columns/buildings. Zero art assets.
3. First-person character: walk, sprint, jump (may be based on the First Person template).
4. GameMode + lobby: host creates a game (listen server), a second player joins by IP; both see each other on the map (replicated position and character model).
5. Player nickname visible above the other player's character.
6. Voice chat: Unreal's built-in VOIP (`bEnableVOIP` in config) — players can hear each other. No proximity/attenuation — working audio is enough.

## Verification criteria

- Two game instances on one machine (PIE: 2 players, Net Mode: Play As Listen Server) — players see each other and movement replicates smoothly.
- Restarting a session requires no manual cleanup — host creates, client joins, works every time.

## Constraints

- Do not add plugins or external dependencies (EOS, Steam) — join-by-IP and built-in VOIP are enough for a prototype.
- Do not design systems "for later" (medals, voting, drunk levels). YAGNI.
