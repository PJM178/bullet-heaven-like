# Projectiles

Status: v1, 2026-11-15. Documents projectiles **as built**. Why it is
shaped this way: [design/projectiles.md](../design/projectiles.md).

## Overview

`ProjectileManager` is the only code that moves projectiles. Each physics
step it moves every projectile, tests the step's path against walls
(`WorldObstacles.IsLineBlocked`), hits enemies through the wall-aware
`EnemyManager.QueryCircle`, each enemy at most once (hit memory of 16 ids
per projectile), up to `Pierce`, stops at the first prop
(`BreakableManager`), and flies a returning projectile back to its thrower.

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
| `ProjectileManager` | `weapons/projectiles/ProjectileManager.cs` | `Spawn(definition, position, direction, ...)`; two node pools (meshes, sprites), each `MaxProjectiles` (256), dressed from the definition on spawn. Returns false if the pool is full. |
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
| 2026-11-15 | v1 | First systems doc; technical content moved here from design/projectiles.md. |
