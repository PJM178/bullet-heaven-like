# Pickups — Architecture

Status: v1, 2026-11-14. Decided and built; how it works today is in
[systems/pickups.md](../systems/pickups.md). This doc keeps the reasoning.

## Overview

Every pickup has four parts that vary independently: its **look**, **how it
is collected**, **what it does**, and **where it comes from**. XP orbs differ
from everything else in all of them at once (thousands of them, pulled in by
a magnet, merged when the pool is full), while coins, hearts and later
pickups are rare, collected by touch, and differ only in data.

## Decisions

1. **One `PickupDefinition` resource per kind of pickup** (a `.tres`, like
   `SkillDefinition`): look, effect, amount. A new pickup kind is a new file,
   not new code. The five coin tiers are five definitions.
2. **One `PickupManager` for every pickup except XP orbs.** Arrays, one loop,
   a pooled sprite per pickup, collected by touch. `Drop(definition,
   position)` is the single entry point: enemy loot, chests, debug and
   placed pickups all use it.
3. **Effects in one place:** `PlayerStats.Collect` switches on an effect
   enum. Simple, readable, and one answer to "what does this pickup do?".
   If effects outgrow a switch, they become one resource subclass per effect.
4. **Pickup ranges are player stats** (`PlayerStats.PickupRadius`,
   `MagnetRadius`), not manager settings, so an upgrade changes one value
   and every system follows.
5. **XP orbs stay on their own path** (`XpManager`, one MultiMesh). Folding
   them in would slow them down or complicate the general manager.
6. **Hand-placed pickups** are `PlacedPickup` markers that hand themselves to
   the manager when the game starts, so placed and dropped pickups behave
   the same.
7. **Coins have no magnet:** they are collected by walking over them.
8. **No shared homing helper:** with one user (XP) it would be an
   abstraction without a second case.

## Open questions

- [ ] Do pickups ever expire or despawn when far behind the player?
- [ ] Should a pickup show a collect effect or sound, and is that part of
      its definition?
- [ ] Do placed pickups need an editor preview, or is a marker enough?

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-14 | v1 | Architecture decided and built: definitions, PickupManager, effects in PlayerStats, radii on the player, XP separate. |
