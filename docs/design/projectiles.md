# Projectiles

Status: decided and built, 2026-11-14.

## Problem

Projectiles were moved in two places: `ProjectileManager` (shards; no
committed weapon used it any more) and a local research throw weapon that
flew, hit and drew its own shots. Each copy had to remember every rule a
projectile must follow; the research copy flew through walls after walls
started blocking attacks. Rules written once per system don't hold up.

## Decisions

- **One projectile system.** `ProjectileManager` is the only code that
  moves projectiles. Everything a projectile must respect is done there,
  once: the step's path is tested against walls, enemies are hit through
  the wall-aware `QueryCircle`, each enemy at most once (hit memory), up to
  `Pierce`, props stop it, and a returning one flies back to its thrower.
- **`ProjectileDefinition` describes any projectile:** flight (`Speed`,
  `Range` in metres instead of a lifetime, `Height`, `Returns`), hitting
  (`Damage`, `Radius`, `Pierce`: 0 = first enemy, -1 = never stops), looks
  (a `Mesh` pointing along its flight, or a `Texture` facing the camera
  with `Size`, `SpinTurnsPerSecond`, `TextureAngleDegrees`).
- **`ProjectileWeapon` is the shared firing part:** cooldown, volley size,
  fan spread, delay between shots, targeting (`ClosestEnemies` sends each
  projectile at its own enemy), sound. It finds the managers by group, so
  weapons added at runtime need no NodePaths. Subclasses override
  `BuildProjectile` / `BuildSound` to build their definition in code.
- Two node pools in the manager (meshes, sprites), each `MaxProjectiles`
  big, dressed from the definition on spawn.
- Props stop every projectile, piercing ones too (they are solid).
- A returning projectile that can't catch up gives up after 3 x `Range`.

## Open questions

- Upgrades that change a projectile per shot (damage, pierce +1): copy
  the definition per weapon level, or add per-shot overrides to `Spawn`.
- Height is only for drawing; flying over low props isn't modelled.

## Changelog

| Date | Change |
| --- | --- |
| 2026-11-14 | One projectile system: ProjectileManager flies everything; ProjectileDefinition extended; ProjectileWeapon added; research throw weapons ported. |
