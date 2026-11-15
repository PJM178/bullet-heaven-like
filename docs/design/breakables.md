# Breakable props

Status: v1.1, 2026-11-15. Decided and built; how it is built is in
[systems/breakables.md](../systems/breakables.md). This doc keeps the reasoning.

## Overview

Props such as barrels, crates and pots that weapons can break, dropping
loot. Built generic from the start: the barrel is the first kind, not a
special case.

## Decisions

1. **Each kind of prop is data**: model, health, size, how it breaks
   apart, dust, loot. Like pickups, a crate is a new file, not new code.
2. **One system owns every prop.** Props are few and don't move.
3. **Weapons don't aim at props.** Auto-aim stays on enemies; a prop is
   hit when it is caught in an attack anyway (sword swing, hammer smash,
   shockwave, projectiles). So weapons never waste attacks on a barrel
   while enemies close in.
4. **Props block the player** (and, since enemy navigation step 1,
   enemies too; see [enemy-navigation.md](enemy-navigation.md)).
5. **Break pieces are cut from the model automatically**: a top, a bottom
   and slices around it like a cake. Any model breaks with no hand-made
   pieces.
6. **Pieces fly with simple scripted motion** (fall, one bounce, spin,
   shrink away), not real physics: cheaper on the phone, predictable, and
   enough for something that lives a second.
7. **Loot uses the shared loot tables** ([pickups.md](pickups.md),
   decision 8), so props, chests and events drop loot the same way.
8. **Models are optional:** without the local model file a plain cylinder
   stands in, so a fresh clone of the repository still works.

## Open questions

- [ ] Do props respawn, or are they placed by a level generator later?
- [ ] A break sound, and is it part of the prop's data?
- [ ] Coins pop out in an arc instead of appearing on the ground?

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-14 | v1 | Architecture decided and built; the barrel is the first breakable. |
| 2026-11-15 | v1.1 | Implementation detail moved to the systems doc; loot through shared loot tables; props block enemies (was an open question). |
