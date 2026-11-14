# World obstacles

Status: v2, 2026-11-14. Documents walls and solid props **as built**: how
they stop enemies and attacks. Why it is shaped this way:
[design/enemy-navigation.md](../design/enemy-navigation.md).

## Overview

Walls and unbroken breakable props are kept as simple ground shapes in
`WorldObstacles`. They are used two ways:

- **Movement.** Enemies can't walk through them. They walk straight at the
  player; a step into something solid puts them back just outside it, so
  they slide along walls and pile up against them. No pathfinding yet: they
  find a doorway only if it lies on their way.
- **Attacks.** Shapes marked `BlocksAttacks` (walls; not barrels, which are
  hit instead) stop anything that would pass through them. This is built
  into the shared pieces, so a weapon gets it by using them:

| Shared piece | Line tested | Covers |
| --- | --- | --- |
| `EnemyManager.QueryCircle` / `QueryRing` / `HitTest` | query centre -> each enemy caught | every area, melee and projectile hit (sword, hammer, waves, any new weapon) |
| `EnemyManager.FindClosest` (both) | point -> candidate | auto-aim: enemies behind walls aren't picked |
| `BreakableManager.Damage...` / `HitTest` | attack centre -> prop | props |
| `WeaponTargeting.SpotAhead` | player -> spot ahead, clipped 0.3 m short of the first wall | fallback landing spots (hammer, area spells) |
| Enemy contact (in `EnemyManager`) | enemy -> player, for enemies within `ContactRange` | contact damage |

  Projectiles: `ProjectileManager` is the only code that moves them (see
  [design/projectiles.md](../design/projectiles.md)), and tests each step's
  path with `IsLineBlocked(previous, next)`, so every projectile weapon is
  covered.

The player is stopped by the same walls through Godot physics (the
StaticBody3D the shapes come from).

## Pieces

| Piece | Where | Role |
| --- | --- | --- |
| `WorldObstacles` | `core/`, node `WorldObstacles` in `main.tscn` | Circles and boxes in a 2 m grid. `AddCircle`, `AddBox` (each with `blocksAttacks`), `Remove`, `PushOut`, `IsLineBlocked`, `ClipLine`. Group `world_obstacles`. |
| `ObstacleBlocker` | `core/` | Child of a StaticBody3D; registers its shapes (box -> box; cylinder, sphere, capsule -> circle) on start, removes them when it leaves the tree. `BlocksAttacks` export (default true). |
| `EnemyManager.Obstacles` | `enemies/` | `PushOut(position, BodyRadius)` after each move; contact needs a clear line; hit queries and `FindClosest` leave out enemies behind walls; `CanReach(from, i)` for any other line. |
| `ProjectileManager.Obstacles` | `weapons/projectiles/` | Tests each step's path; a blocked projectile is removed. |
| `BreakableManager.Obstacles` | `props/breakables/` | Adds each prop's cylinder (not blocking attacks), removes it on break; area and swing hits need a clear line to the prop. |

Users now: the small house (`ObstacleBlocker` under its `Collision`) and the
barrels.

## How the line test works

`IsLineBlocked(from, to)` is a 2D ray test on the ground, not a physics
raycast: look up the grid cells around the line, skip shapes that don't
block attacks, and test the rest. Circle: closest point on the line closer
than the radius. Box: the line is turned into the box's frame and clipped
against its two pairs of sides (slab test); anything left means it passes
through. `IsLineBlocked` stops at the first shape found; `ClipLine` finds
the nearest entry point (circle: smaller root of the line-circle quadratic;
box: the slab test's entry) and returns the point just before it.
Allocates nothing.

| Value | Default | On |
| --- | --- | --- |
| `CellSize` | 2 m | `WorldObstacles` |
| `BodyRadius` | 0.3 m (basic enemy) | each `EnemyDefinition` |

`PushOut` checks every cell the enemy's circle overlaps (usually one, up
to four), so any body radius works.

Checks (local): `research/tests/enemy_walls_test.gd` (headless; 150
enemies, player in the house, reports any enemy inside a wall),
`research/tests/enemy_walls_shot.tscn` (screenshots to `SHOT_DIR`),
`research/tests/enemy_kinds_test.gd` (a second, bigger kind with a small
pool), `research/tests/wall_block_test.gd` (line tests through the wall,
doorway and barrel; no contact damage through the wall),
`research/tests/sealed_room_test.gd` (doorway sealed, each weapon alone for
10 s with enemies outside: any kill is a leak).

## Open questions

- XP orbs and pickups are pulled through walls by the magnet radius.
- See also [design/enemy-navigation.md](../design/enemy-navigation.md).

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-14 | v1 | EnemyObstacles, EnemyBlocker; house walls and barrels block enemies. |
| 2026-11-14 | v1.1 | Body radius per enemy kind (EnemyDefinition); MaxAgentRadius removed. |
| 2026-11-14 | v2 | Walls block attacks, projectiles and contact (`IsLineBlocked`, `BlocksAttacks`); renamed to WorldObstacles / ObstacleBlocker and moved to `core/`. |
| 2026-11-14 | v2.1 | Wall check moved into the hit queries (no per-weapon checks); `ClipLine` + `WeaponTargeting.SpotAhead` for fallback spots. |
| 2026-11-14 | v2.2 | All projectiles fly through ProjectileManager, so its path test covers every projectile weapon. |
