# Walk-in spaces

Status: v1, 2026-11-14. Documents walk-in spaces **as built**. Why it is
shaped this way: [design/walk-in-spaces.md](../design/walk-in-spaces.md).

## Overview

A walk-in space is a place the player enters on foot, like the small
house. Walls block the player (the doorway is open). While the player is
inside, the roof disappears and the walls and posts on the camera's side
are cut down to 0.5 m stubs, so the room is seen from above while its
outline and doorway stay visible. Leaving restores them.

## In main.tscn

| Node | Where | What |
| --- | --- | --- |
| House | (30, -35) | `OptionalScene` loading `local_assets/buildings/house_small.tscn` (local only); door on +z |

## Implementation notes

`WalkInSpace` (`props/walk_in/`) is an Area3D script. Put it on the node
whose first `CollisionShape3D` covers the inside (ideally a `BoxShape3D`).
It reacts only to the `Player` body, and does nothing while no player is
inside.

| Export | Default | Meaning |
| --- | --- | --- |
| `AlwaysHide` | none | Nodes hidden whenever the player is inside (roof) |
| `FacingPieces` | none | Containers; each child is hidden when its local +Z faces the camera (walls) |
| `EdgePieces` | none | Containers; each child is hidden when it stands on the camera's side of the centre (posts) |
| `FacingThreshold` | 0.1 | Dot product above which a wall counts as facing the camera |
| `EdgeThreshold` | 0.1 | Fraction of the box half size (metres if not a box) past the centre toward the camera |
| `StubHeight` | 0.5 m | What stays of a cut wall or post, above its base; 0 hides them fully |

Stubs: each cut mesh surface gets a surface override material using
`props/walk_in/cutaway_stub.gdshader` (discards pixels above `stub_height`
in the piece's own space, draws both sides). One stub material is made per
original material and copies its albedo texture and colour; non-standard
materials get a grey stub. Materials change only when a piece's state
changes, not every frame. The stubs' tops are open (you see into the wall).

The small house: `Inside` lists `Roof`, `Walls` and `Posts`. With the
default camera, the south and east walls, the posts along them, and the
roof are cut; the two corner posts where the cut ends stay whole.

Check: `research/tests/walk_in_test.gd` (local) runs headless, puts the
player inside and prints the roof's visibility and which pieces are cut.
`research/tests/walk_in_shot.tscn` saves game-camera shots outside, at the
door and inside to `SHOT_DIR`.

## Open questions

See [design/walk-in-spaces.md](../design/walk-in-spaces.md).

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-14 | v1 | `WalkInSpace` cutaway; small house placed where the Shrine marker was. |
| 2026-11-14 | v1.1 | Cut walls and posts become 0.5 m stubs instead of vanishing, so the doorway stays visible. |
