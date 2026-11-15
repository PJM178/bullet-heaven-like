# Pickups — Architecture

Status: v1.2, 2026-11-15. Decided and built; how it works today is in
[systems/pickups.md](../systems/pickups.md). This doc keeps the reasoning.

## Overview

Every pickup has four parts that vary independently: its **look**, **how it
is collected**, **what it does**, and **where it comes from**. XP orbs differ
from everything else in all of them at once (thousands of them, pulled in by
a magnet, merged when the pool is full), while coins, hearts and later
pickups are rare, collected by touch, and differ only in data.

## Decisions

1. **Each kind of pickup is data** (look, effect, amount), like skills. A
   new pickup kind is a new file, not new code. The five coin tiers are
   five kinds.
2. **One system for every pickup except XP orbs**, and one way for a
   pickup to enter the world: enemy loot, chests, debug and placed
   pickups all drop through it.
3. **What each pickup does is decided in one place**, so "what does this
   pickup do?" has one answer.
4. **Pickup ranges are player stats**, not settings of the pickup systems,
   so an upgrade (like the Magnet) changes one value and everything
   follows.
5. **XP orbs stay separate.** There are thousands of them, pulled in by a
   magnet and merged when there are too many; folding them in would slow
   them down or complicate everything else.
6. **Pickups placed by hand in a level behave exactly like dropped ones.**
7. **Coins have no magnet:** they are collected by walking over them.
8. **Loot comes from loot tables** (2026-11-15): lines of "this chance of
   dropping this many of this pickup", rolled in one shared place.
   Breakables, chests, events and any later source each just hold a
   table; a table can be shared, so tuning it changes every source using it.
9. **No shared homing for pickups:** only XP orbs fly to the player, so a
   general version would be an abstraction without a second use.

## Open questions

- [ ] Do pickups ever expire or despawn when far behind the player?
- [ ] Should a pickup show a collect effect or sound, and is that part of
      its definition?
- [ ] Do placed pickups need an editor preview, or is a marker enough?

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-14 | v1 | Architecture decided and built: definitions, PickupManager, effects in PlayerStats, radii on the player, XP separate. |
| 2026-11-15 | v1.1 | Decision 8: LootTable + DropLoot, one place for loot from any source. |
| 2026-11-15 | v1.2 | Class names left to the systems doc. |
