# Breakable props — Architecture

Status: v1, 2026-11-14. Decided and built; how it works today is in
[systems/breakables.md](../systems/breakables.md). This doc keeps the reasoning.

## Overview

Props such as barrels, crates and pots that weapons can break, dropping
loot. Built generic from the start: the barrel is the first definition, not
a special case.

## Decisions

1. **One `BreakableDefinition` per kind of prop** (`.tres`): model path,
   health, size, slice count, dust effect, loot. Same pattern as
   `PickupDefinition`; a crate is a new file, not new code.
2. **One `BreakableManager`** owns every prop. Props are few and static,
   so slots are kept for the whole run (no swap remove).
3. **Weapons don't aim at props.** Auto-aim stays on enemies; a prop is hit
   when caught in an attack anyway (sword wedge, hammer smash, shockwave,
   projectiles). Each weapon calls the manager next to its enemy hits.
   Chosen so weapons never waste attacks while enemies close in.
4. **Props block the player only**: one `StaticBody3D` cylinder each.
   Enemies don't use physics, and steering around obstacles isn't worth
   the per-enemy cost yet.
5. **Pieces are cut from the model in code** (`MeshShatter`): triangles
   facing up or down go to a top/bottom piece, the rest to cake slices
   around the upright axis. Works for any model with no hand-made pieces,
   and keeps source-specific steps out of the converter.
6. **Pieces move by simple scripted motion** (gravity, one bounce, spin,
   shrink away), from a node pool. No rigid bodies: cheaper on the phone,
   predictable, enough for something that lives a second.
7. **Loot is a list of `LootDrop` lines** (pickup, chance, count range),
   each rolled on its own and dropped through `PickupManager.Drop`.
   `LootDrop` lives in `pickups/` so enemies or chests can reuse it.
8. **Models load by path** (local_assets is git-ignored); without the file
   a plain cylinder stands in, so a fresh clone still works.

## Open questions

- [ ] Should enemies walk around props (or be blocked by them)?
- [ ] Do props respawn, or are they placed by a level generator later?
- [ ] A break sound, and is it part of the definition?
- [ ] Coins pop out in an arc instead of appearing on the ground?

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-14 | v1 | Architecture decided and built; the barrel is the first breakable. |
