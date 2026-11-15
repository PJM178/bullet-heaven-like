# Enemy navigation

Status: step 1 (walls stop enemies and attacks) built 2026-11-14; step 2
(finding the way in) planned. How it is built:
[systems/world-obstacles.md](../systems/world-obstacles.md).

## Problem

Walls and props didn't stop enemies. Walk-in houses need walls that hold
the horde back, with the doorway as the way in.

## Cost

Hundreds of enemies (300-600) have to stay cheap on the phone, which rules
out giving each enemy its own physics body or its own pathfinding: both
grow too expensive with the enemy count. Chosen instead: simple wall
shapes that enemies are pushed out of after they move (cheap per enemy),
and later one shared "which way to the player" map that every enemy reads.
The comparison is in the systems doc.

## Decisions

- **Step 1: walls stop enemies.** Enemies walk straight at the player and
  slide along anything solid. Rule of thumb: what blocks the player
  should block enemies (house walls, unbroken props).
- **Walls block attacks too** (2026-11-14): projectiles, enemy contact
  damage, melee and area hits, and auto-aim don't go through walls. Props
  such as barrels don't block attacks: they are hit instead.
- The obstacle store is shared by enemies, attacks and later pathfinding,
  not owned by the enemies.
- **Step 2, finding the way in (planned).** One shared map of directions
  toward the player, fine enough that a doorway stays open, which enemies
  follow near obstacles; in the open they keep walking straight.

## Open questions

- Enemies can spawn inside a closed room (not inside a wall). Spawning
  should probably skip enclosed spots; easy once step 2 knows which places
  are reachable.
- Until step 2, enemies pile against the wall nearest the player instead
  of finding the door.
- Moving obstacles (doors that open) aren't supported yet.
- Phone cost not measured yet; expected well under 1 ms.

## Changelog

| Date | Change |
| --- | --- |
| 2026-11-14 | Costs compared; obstacle push-out chosen and built; flow field planned. |
| 2026-11-14 | Walls block attacks, projectiles and contact; renamed to WorldObstacles / ObstacleBlocker. |
| 2026-11-15 | Technical detail (cost table, line test, flow-field plan) moved to systems/world-obstacles.md. |
