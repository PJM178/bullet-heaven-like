# Enemy navigation

Status: step 1 (collision) built 2026-11-14; step 2 (pathfinding) planned.
As built: [systems/world-obstacles.md](../systems/world-obstacles.md).

## Problem

Enemies are plain data in `EnemyManager` arrays (no physics bodies), so
walls and props didn't stop them. Walk-in houses need walls that hold the
horde back, with the doorway as the way in.

## Cost estimates (before building)

| Approach | Cost with 300-600 enemies | Verdict |
| --- | --- | --- |
| `CharacterBody3D` + `MoveAndSlide` per enemy | Several shape casts and engine calls each per step; likely several ms on the phone; breaks the pooled arrays | No |
| `PhysicsServer3D` shape query per enemy | One engine call each per step | Possible, not ideal |
| Own obstacle shapes in a grid, push out after moving | A dictionary lookup and a few sums per enemy | **Chosen** |
| Pathfinding: `NavigationAgent3D` per enemy | Grows with enemy count; crowd avoidance is heavy | No |
| Pathfinding: one shared flow field from the player | One BFS over a grid near the player when the player changes cell; O(1) per enemy | **Planned** |

## Decisions

- **Step 1, collision.** `WorldObstacles` holds circles and boxes (turned
  about the vertical only) in a 2 m grid. `EnemyManager` moves each enemy,
  then `PushOut` moves it out of anything it entered, using the enemy's
  own `BodyRadius` ([enemy-definitions.md](enemy-definitions.md)). Height
  is ignored.
- Obstacles come from two places: `ObstacleBlocker`, a child node that turns
  its parent body's collision shapes into obstacles (the house walls), and
  `BreakableManager`, which adds each prop's cylinder and removes it on
  break. Rule of thumb: what blocks the player should block enemies.
- `WorldObstacles` is its own node, not part of `EnemyManager`, so the
  flow field (step 2) can read the same shapes.
- **Walls block attacks too** (decided 2026-11-14): projectiles, enemy
  contact, sword, hammer smash and shockwave, and auto-aim test a line
  against shapes marked `BlocksAttacks` (`WorldObstacles.IsLineBlocked`, a
  2D ray test on the ground, not a physics raycast). Barrels don't block
  attacks: they are hit instead. The hammer's auto-aim needs sight too.
  Because the store now serves more than enemies, it was renamed
  `WorldObstacles` (`EnemyBlocker` -> `ObstacleBlocker`) and moved to `core/`.
- **Step 2, pathfinding (later).** A flow field: rasterise the obstacles
  into a fine grid (about 0.5 m, so a 1.43 m doorway stays open), BFS
  outward from the player's cell, enemies follow the field near obstacles
  and keep steering straight elsewhere.

## Open questions

- Enemies spawn on a ring and can appear inside a closed room (not inside
  a wall: they're pushed out). Spawning should probably skip blocked or
  enclosed spots; becomes easy once the flow field knows reachable cells.
- Until step 2, enemies pile against the wall nearest the player instead
  of finding the door.
- Obstacles are read once; moving obstacles (doors that open) need
  `Remove` + `Add` or a dedicated update call.
- Phone cost not measured yet; expected well under 1 ms.

## Changelog

| Date | Change |
| --- | --- |
| 2026-11-14 | Costs compared; obstacle push-out chosen and built; flow field planned. |
| 2026-11-14 | Walls block attacks, projectiles and contact; renamed to WorldObstacles / ObstacleBlocker. |
