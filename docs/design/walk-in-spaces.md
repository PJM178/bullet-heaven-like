# Walk-in spaces

Status: decided, built as v1 (2026-11-14). As built:
[systems/walk-in-spaces.md](../systems/walk-in-spaces.md).

## Problem

Some places are entered on foot in the open world, not loaded as a
separate map: walk-in houses now, and later caves, ruins and covered
halls. The camera is high and looks down at about 30 degrees, so once the
player steps in, the roof and the walls nearest the camera hide the whole
room (a 3.36 m wall hides about 5.8 m of floor behind it).

## Options considered

1. Hide only the roof. Not enough: the near walls still hide the room.
2. **Cutaway (chosen)**: hide the roof and the walls and posts on the
   camera's side while the player is inside. Cheap (only visibility
   changes), and it reads clearly: you look into the room like a dollhouse.
3. Fade those pieces instead of hiding them. Nicer, but every faded piece
   is a transparent draw on mobile and needs its materials changed.
   Could be added later as a mode of the same component.

## Decisions

- One reusable component, `WalkInSpace`, on the Area3D that covers the
  inside. It knows nothing about houses: a space lists its pieces in
  three groups, and any level geometry can use it.
  - *Always hide*: roofs, ceilings, upper floors.
  - *Facing pieces*: hidden when their outer face (local +Z, the kit's
    wall convention) points toward the camera. Walls.
  - *Edge pieces*: hidden when they stand on the camera's side of the
    centre. Posts, pillars, anything without a clear facing.
- Containers are listed, not single pieces, so a generated building only
  names its `Walls`, `Posts` and `Roof` nodes.
- Edge pieces are measured in fractions of the inside box's half size, so
  a long room treats all its corners alike: the corner posts where the cut
  ends stay and cover the cut wall ends.
- The direction is the camera's own (its flattened back axis), not the
  direction from the room to the camera, so the cut is the same wherever
  the room is on screen. It is re-chosen every frame while the player is
  inside, so a turning camera still works.
- Cut walls and posts are **lowered to stubs** (0.5 m, a shader discards
  everything above), not hidden. Hiding them fully lost the room's
  outline: once inside, you could no longer see where the doorway was.
  The stub keeps the piece's own texture (no squashing, unlike scaling it
  down). Roofs are still hidden outright.
- Only visuals change. Collision is on separate nodes and stays.
- Enemies are not physics bodies; walls stop them through
  `WorldObstacles` (see [enemy-navigation.md](enemy-navigation.md)).

## Open questions

- Fade mode (option 3) for spaces where hiding pops too much.
- Stub tops are open (you look into the hollow wall). A cap colour could
  be drawn on the cut if it reads badly.
- Should hidden walls still cast shadows (`CastShadow = ShadowsOnly`)?
  Moot while the main light has no shadows.
- Spaces inside spaces (a room in a cave): each its own `WalkInSpace`
  should work, but untested.
- The house generator (`kit_to_gltf.py house`) could attach the component
  itself, so regenerated scenes keep it.

## Changelog

| Date | Change |
| --- | --- |
| 2026-11-14 | Cutaway chosen; `WalkInSpace` built and used on the small house. |
| 2026-11-14 | Cut pieces become low stubs instead of vanishing, so doorways stay readable. |
