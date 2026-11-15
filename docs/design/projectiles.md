# Projectiles

Status: decided and built, 2026-11-14. How it is built:
[systems/projectiles.md](../systems/projectiles.md).

## Problem

Projectiles used to be moved in more than one place, and each copy had to
remember every rule a projectile must follow. One copy flew through walls
after walls started blocking attacks. Rules written once per weapon don't
hold up.

## Decisions

- **One projectile system.** Every projectile in the game, whichever
  weapon fires it, flies, hits and is blocked by the same rules, written
  once. A new projectile weapon gets walls, piercing, props and returning
  for free.
- **Projectiles are data.** What a projectile is (speed, how far it flies,
  damage, size, how many enemies it passes through, whether it comes back
  like a boomerang, how it looks) is a definition, so a new projectile is
  a new file, not new code.
- **Range is a distance, not a lifetime**, so a faster projectile doesn't
  reach further by accident.
- **Firing is shared too:** cooldown, volley size, fan spread, delay
  between shots and targeting (each projectile at its own enemy, if wanted)
  are one shared weapon part that projectile weapons build on.
- **Props stop every projectile**, piercing ones too: they are solid.
- **A returning projectile that can't catch up with its thrower gives up**
  after a while instead of chasing forever.

## Open questions

- Upgrades that change a projectile per shot (damage, pierce +1): how they
  are applied is open (see the systems doc).
- Projectiles have a height only for looks; flying over low props isn't
  part of the game.

## Changelog

| Date | Change |
| --- | --- |
| 2026-11-14 | One projectile system: ProjectileManager flies everything; ProjectileDefinition extended; ProjectileWeapon added; research throw weapons ported. |
| 2026-11-15 | Technical detail moved to systems/projectiles.md. |
