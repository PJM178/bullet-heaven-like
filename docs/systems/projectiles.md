# Projectiles

Status: v1.1, 2026-11-22. Documents projectiles **as built**. Why it is
shaped this way: [design/projectiles.md](../design/projectiles.md).

## Overview

Split in two, like the enemies (core and shell):

- **`ProjectileSimulation`** (core, plain C# class) is the only code that
  moves projectiles. Each physics step it moves every projectile, tests the
  step's path against walls (`IObstacleQuery.IsLineBlocked`), hits enemies
  through the wall-aware `IEnemyTargets.QueryCircle` / `Damage`, each enemy
  at most once (hit memory of 16 ids per projectile), up to `Pierce`, stops
  at the first prop (`IBreakableTargets.HitTest`), and flies a returning
  projectile back to its thrower (a player index; player positions are
  passed to `Step`). Never touches nodes.
- **`ProjectileManager`** (shell, the node) owns one, steps it, and draws
  it: one `MeshInstance3D` and one `Sprite3D` per slot, the one that fits
  the definition shown. `EnemyManager`, `BreakableManager` and
  `WorldObstacles` implement the three interfaces, so a projectile's hit
  goes through `EnemyManager.Damage` and the flash shows at once.

No committed weapon fires projectiles today; the shard (`shard.tres`) is
the committed example definition. Local research throw weapons use the
system.

## ProjectileDefinition

| Group | Field | Meaning |
| --- | --- | --- |
| Flight | `Speed` | m/s |
| | `Range` | metres flown before it ends (instead of a lifetime) |
| | `Height` | drawing height only; flying over low props isn't modelled |
| | `Returns` | flies back to the thrower after `Range`; gives up after 3 x `Range` travelled |
| Hitting | `Damage` | per hit |
| | `Radius` | hit radius, m |
| | `Pierce` | enemies passed through: 0 = stops at the first, -1 = never stops |
| Looks | `Mesh` | a mesh pointing along its flight, or |
| | `Texture`, `Size`, `SpinTurnsPerSecond`, `TextureAngleDegrees` | a camera-facing sprite |

Props stop every projectile, piercing ones too.

## ProjectileWeapon

The shared firing part (`weapons/projectiles/ProjectileWeapon.cs`), a
weapon node under the Player switched on by its skill. Every `Cooldown`
seconds it fires `Count` projectiles in a fan `SpreadDegrees` wide,
`ShotDelay` apart, aimed by `Targeting` (`ClosestEnemies` sends each
projectile at its own enemy, the closest first) within `TargetRange`, with
an optional `Sound`. It finds the managers by group, so weapons added at
runtime need no NodePaths. Subclasses override `BuildProjectile` /
`BuildSound` to build their definition in code.

## Implementation notes

| Piece | Where | Role |
| --- | --- | --- |
| `ProjectileSimulation` | `weapons/projectiles/ProjectileSimulation.cs` | Core: projectile data (swap remove, a fixed slot each), `Spawn` (returns the index or `NoProjectile`), `Step(delta, players)`, `RemovedSlots`. |
| `ProjectileManager` | `weapons/projectiles/ProjectileManager.cs` | Shell: `Spawn(definition, position, direction, returnTo)` (`returnTo` a node, mapped to a player index, up to 8 throwers); per slot a mesh and a sprite node, `MaxProjectiles` (256) slots, dressed from the definition on spawn; picture spin is looks-only, advanced per rendered frame. Returns false if full. Physics priority 10 (after enemies). |
| `IEnemyTargets`, `IBreakableTargets` | `enemies/`, `props/breakables/` | What the core needs from enemies (QueryCircle, GetId, Damage) and props (HitTest); implemented by `EnemyManager` and `BreakableManager`. |
| `ProjectileDefinition` | `weapons/projectiles/ProjectileDefinition.cs` | Resource, see above. |
| `ProjectileWeapon` | `weapons/projectiles/ProjectileWeapon.cs` | Firing, see above. |

## Planned changes

None decided.

## Open questions

- [ ] Upgrades that change a projectile per shot (damage, pierce +1): copy
      the definition per weapon level, or add per-shot overrides to `Spawn`.

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-22 | v1.1 | Split into ProjectileSimulation (core) and ProjectileManager (shell); IEnemyTargets / IBreakableTargets; returning projectiles target a player index; one mesh + one sprite node per slot. Behaviour unchanged. |
| 2026-11-15 | v1 | First systems doc; technical content moved here from design/projectiles.md. |
