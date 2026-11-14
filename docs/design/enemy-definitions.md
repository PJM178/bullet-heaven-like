# Enemy definitions

Status: decided and built, 2026-11-14.

## Problem

Everything about an enemy was one value on `EnemyManager` (one scene, one
speed, health, hit radius, body radius...), so the game could only have one
kind of enemy. A second kind (a big brute, a small rat) needs its own size,
speed and looks.

## Decisions

- **`EnemyDefinition`** resource per kind, saved under `enemies/kinds/`, like
  `BreakableDefinition` and `PickupDefinition`: `Scene` (+ `FallbackScene`
  when local sprite frames are missing), `Speed`, `MaxHealth`,
  `ContactDamage`, `ContactRange`, `XpPerKill`, `HitRadius` (weapons),
  `BodyRadius` (walls), `PoolSize`, `CorpsePoolSize`.
- **One node pool per definition**, chosen over one shared pool that swaps
  models: kinds have different scenes, and swapping models at spawn would
  mean instancing at run time. Each kind's `PoolSize` caps how many of it
  can be alive; all pools together are `EnemyManager.MaxEnemies`.
- Live enemies stay packed in `EnemyManager`'s arrays (swap remove) with a
  kind index each. Idle nodes wait on a per-kind stack; despawning pushes
  the node back, so the live arrays and the node pools are independent.
- Callers ask an enemy's size: `GetHitRadius(i)` (sword wedge, wave arcs).
- `WorldObstacles.PushOut` takes the radius per call and checks every cell
  the circle overlaps, so any `BodyRadius` works without a global maximum.
- Which kind spawns: for now `EnemyManager.Kinds` + `SpawnWeights` (a
  missing weight is 1), among kinds with idle nodes. A wave/spawn system
  can take this over later.
- Crowd settings stay on the manager: `SeparationRadius`,
  `SeparationStrength`, `StopDistance`, `ArriveSlowdown`.

## Open questions

- Separation should probably use the two enemies' `BodyRadius` sums
  instead of one `SeparationRadius` (big enemies overlap small ones now).
- `StopDistance` per kind (a big enemy should stop further out).
- A wave system: which kinds spawn when, and how many.
- Pool sizes cost memory and start-up time per kind; size them per map.

## Changelog

| Date | Change |
| --- | --- |
| 2026-11-14 | EnemyDefinition, per-definition pools, per-kind hit/body radii; MaxAgentRadius removed. |
