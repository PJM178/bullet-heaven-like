# Enemies

Status: v1, 2026-11-15. Documents enemy kinds, pools and spawning **as
built**. Why it is shaped this way:
[design/enemy-definitions.md](../design/enemy-definitions.md). Walls and
movement blocking: [world-obstacles.md](world-obstacles.md).

## Overview

Each kind of enemy is an `EnemyDefinition` (`enemies/kinds/*.tres`).
`EnemyManager` keeps every live enemy as plain data in packed arrays (swap
remove), each with a kind index, and gives each kind its own pool of idle
nodes. Enemies walk straight at the player, keep apart from each other,
and are pushed out of walls after each move.

## Kinds

| Kind | Speed | Health | Contact | XP | Hit r | Body r | Pool |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Basic enemy | 2 m/s | 2 | 1 heart within 0.9 m | 100 | 0.25 m | 0.3 m | 600 (+64 corpses) |

## EnemyDefinition

| Field | Meaning |
| --- | --- |
| `Scene`, `FallbackScene` | Looks; the fallback is used when local sprite frames are missing. |
| `Speed`, `MaxHealth`, `ContactDamage`, `ContactRange`, `XpPerKill` | Stats. |
| `HitRadius` | Size for weapons (`EnemyManager.GetHitRadius(i)`: sword wedge, wave arcs). |
| `BodyRadius` | Size for walls (`WorldObstacles.PushOut`). |
| `PoolSize`, `CorpsePoolSize` | Nodes made at start; caps how many of this kind can be alive. |

## Pools and spawning

- One node pool per kind. Idle nodes wait on a per-kind stack; despawning
  pushes the node back, so the live arrays and the node pools are
  independent. All pools together are `EnemyManager.MaxEnemies`.
- Which kind spawns: `EnemyManager.Kinds` + `SpawnWeights` (a missing
  weight is 1), chosen among kinds with idle nodes left.
- Spawning keeps `TargetCount` (50) alive, at most `MaxSpawnsPerFrame`
  (10) per frame, on a ring `SpawnDistance` out (22 m in `main.tscn`);
  enemies further behind than `DespawnDistance` (32 m in `main.tscn`) are
  moved back onto the ring.
- Crowd settings are on the manager, shared by all kinds:
  `SeparationRadius` 0.6 m, `SeparationStrength` 6, `StopDistance` 0.7 m,
  `ArriveSlowdown` 1.

## Queries for weapons

`QueryCircle`, `QueryRing`, `HitTest`, `FindClosest` (single and several),
`Damage(i, amount)`, `GetPosition`, `GetId`, `GetHitRadius`, `CanReach`.
All leave out enemies behind walls (see [world-obstacles.md](world-obstacles.md)).

## Open questions

- [ ] Separation uses one `SeparationRadius` for all kinds, so big enemies
      overlap small ones; probably should use the two `BodyRadius` sums.
- [ ] `StopDistance` per kind (a big enemy should stop further out).
- [ ] Pool sizes cost memory and start-up time per kind; size them per map.

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-15 | v1 | First systems doc; technical content moved here from design/enemy-definitions.md. |
