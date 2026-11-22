# Enemies

Status: v1.1, 2026-11-22. Documents enemy kinds, pools and spawning **as
built**. Why it is shaped this way:
[design/enemy-definitions.md](../design/enemy-definitions.md). Walls and
movement blocking: [world-obstacles.md](world-obstacles.md).

## Overview

Each kind of enemy is an `EnemyDefinition` (`enemies/kinds/*.tres`). The
enemy system is split in two (core and shell, see
[research/multiplayer-architecture/code-impact.md](../research/multiplayer-architecture/code-impact.md)):

- **`EnemySimulation`** (core, plain C# class, not a node): every live
  enemy as data in packed arrays (swap remove), each with a kind index and
  a slot. Spawning, steering, separation, walls, contact, damage, kills and
  the weapon queries. Never touches nodes.
- **`EnemyManager`** (shell, the `Node3D` in the scene): owns one
  `EnemySimulation`, steps it every physics tick, and draws it: one pooled
  node per slot, sprite directions, stepped animation, hit flash, corpses.
  Connects it to the game: player position in, contact damage to
  `PlayerStats`, XP drops to `XpManager`. Weapons call its queries, which
  pass straight through to the core.

Enemies walk straight at the nearest player, keep apart from each other,
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

- **Slots.** Every enemy that can exist has a fixed slot, numbered kind by
  kind (kind 0's pool first). The core keeps a stack of free slots per kind;
  the shell keeps one node per slot (`_bodies[slot]`), so a node never has
  to follow an enemy around the swap-removed arrays. Per-kind capacity is
  the definition's `PoolSize` (0 when the kind has no scene). All kinds
  together are `EnemyManager.MaxEnemies` (`EnemySimulation.Capacity`).
- **Each step** (`EnemySimulation.Step(delta, players, contactDamage)`):
  remove enemies killed last step, spawn/remove toward `TargetCount`, build
  the grid, separation, move, rebuild the grid for the weapons. After it the
  shell reads `RemovedSlots` (hide nodes, end flash), then `Spawned` (show
  nodes, random animation start), in that order because a freed slot can be
  reused in the same step, then copies `Positions` / `Velocities` /
  `Headings` to the nodes.
- Which kind spawns: `EnemyManager.Kinds` + `SpawnWeights` (a missing
  weight is 1), chosen among kinds with free slots left.
- Spawning keeps `TargetCount` (50) alive, at most `MaxSpawnsPerFrame`
  (10) per step, on a ring `SpawnDistance` out (22 m in `main.tscn`) around
  a random player; enemies further than `DespawnDistance` (32 m in
  `main.tscn`) from every player are moved back onto a ring.
- The core takes a list of player positions and fills contact damage per
  player. `EnemyManager` passes one player (`Target`) for now.
- Settings are `[Export]`s on `EnemyManager`, copied into the core every
  step (`PushSettings`), so changing them while running still works. Crowd
  settings are shared by all kinds: `SeparationRadius` 0.6 m,
  `SeparationStrength` 6, `StopDistance` 0.7 m, `ArriveSlowdown` 1.
- Other cores reach the enemies through `IEnemyTargets` (`QueryCircle`,
  `GetId`, `Damage`), which `EnemyManager` implements (used by
  `ProjectileSimulation`).
- Walls: the core gets `WorldObstacles` as `IObstacleQuery` (`PushOut`,
  `IsLineBlocked`), so it never depends on the node.
- Randomness: the core takes a `Random` (default `Random.Shared`); a seeded
  one makes spawns repeatable. Looks-only randomness (animation start
  frame) stays in the shell.

## Queries for weapons

`QueryCircle`, `QueryRing`, `HitTest`, `FindClosest` (single and several),
`Damage(i, amount)`, `GetPosition`, `GetId`, `GetHitRadius`, `CanReach`, on
`EnemyManager`, forwarding to `EnemySimulation`. `EnemySimulation.Damage`
returns `None` / `Hit` / `Killed`; the shell adds the flash, corpse and XP
drop. Killed enemies stay in the arrays (marked dead, skipped by queries)
until the start of the next step.
All leave out enemies behind walls (see [world-obstacles.md](world-obstacles.md)).

## Open questions

- [ ] Separation uses one `SeparationRadius` for all kinds, so big enemies
      overlap small ones; probably should use the two `BodyRadius` sums.
- [ ] `StopDistance` per kind (a big enemy should stop further out).
- [ ] Pool sizes cost memory and start-up time per kind; size them per map.

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-22 | v1.1 | Split into EnemySimulation (core) and EnemyManager (shell); slots; core takes several player positions and gives contact damage per player; IObstacleQuery. Behaviour unchanged. |
| 2026-11-15 | v1 | First systems doc; technical content moved here from design/enemy-definitions.md. |
