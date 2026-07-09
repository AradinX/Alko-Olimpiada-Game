# /goal instruction — Alko Olimpiada, Prototype 1

> Paste the block below as the session goal. One prototype = one goal. When done, replace with the next one (Prototype 2: drunk system).

---

## Goal

Build **Prototype 1** of Alko Olimpiada in Unity 6: a hub map with first-person movement and working multiplayer (host + client) using Netcode for GameObjects, for 2 players.

## Context

- Full game description: `GDD-Alko-Olimpiada.md` (read sections 4, 5, 9, 10, 12 before starting; the document is in Polish — translate as needed).
- Unity 6 with URP, new Input System, Netcode for GameObjects (NGO) with Unity Transport.
- Do not build anything beyond the scope below (no minigames, no drunk system, no pickups — those are Prototypes 2–3).

## Scope (definition of done)

1. Unity 6 project `AlkoOlimpiada` (URP template) with the `Assets/` folder structure described in `README.md`.
2. `Hub` scene — greybox only: flat ground plane, a few primitive shapes as placeholder columns/buildings. Zero art assets.
3. First-person character controller: walk, sprint, jump (CharacterController + new Input System).
4. Multiplayer via NGO: host starts a game, a second player joins by IP (Unity Transport); both see each other's character on the map (NetworkTransform replication).
5. Player nickname visible above the other player's character (world-space TextMeshPro billboard).
6. Voice chat: Unity Vivox — players can hear each other. No proximity/attenuation — working audio is enough.

## Verification criteria

- Two game instances on one machine (build + editor, or Multiplayer Play Mode) — players see each other and movement replicates smoothly.
- Restarting a session requires no manual cleanup — host starts, client joins, works every time.

## Constraints

- No third-party networking assets (no Mirror, no Photon) — NGO + join-by-IP is enough for a prototype.
- Vivox is the only Unity Gaming Services dependency; skip Relay/Lobby for now.
- Do not design systems "for later" (medals, voting, drunk levels). YAGNI.
