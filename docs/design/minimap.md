# Minimap and world markers: architecture

Status: v3, 2026-11-14. Minimap chosen over the compass bar; details still
being tuned. How it works today is in [systems/minimap.md](../systems/minimap.md).

## Goal

Give the player a reason and a way to leave the fight: point at places and
events in the world (shrines, chests, ambushes, bosses) and let them choose
what to go after.

## Decisions

1. **A minimap, not a compass bar.** Compass bars suit first-person games,
   where you can't see around you. Our camera is fixed and top-down, so a
   zoomed-out version of that same view fits better, and it shows
   direction and distance at once.
2. **The map turns with the screen, not the character.** Up on the map is
   up on the screen (the camera never turns, except in the death shot). A
   map that turned with facing would spin with every aim of the right stick.
3. **Discovery comes from information.** Each marker has a reveal state:
   Hidden, Rumoured ("?" on the rim, direction only), Sensed (dim icon),
   Discovered (full icon, after being on screen once). States only ever go
   up. Ranges and starting state are per marker kind.
4. **Markers are their own system (`WorldMarkers`), separate from the
   display.** Game systems call `Add` / `Reveal` / `Remove` and never touch
   the UI. The switch from compass bar to minimap changed only the UI.
5. **One `WorldMarkerDefinition` .tres per kind of marker** (looks,
   ranges), like pickups and breakables. A breakable can carry one in its
   definition, and the marker goes away when the prop breaks.
6. **Minimap chosen over the compass bar** (2026-11-14). The compass bar
   is kept but inert (`ui/CompassBar.cs`, hidden and disabled in
   `player_hud.tscn`) until it's deleted. It is a fixed, screen-relative strip with a facing cursor that
   names the marker it points at. 

## Open questions

- [x] Minimap vs. compass bar: minimap.
- [ ] Delete the inert compass bar code?
- [ ] Map range (40 m) and size: enough to plan a detour without clutter?
- [ ] Show enemies (density or dots) so the player can see where the swarm
      is thickening?
- [ ] Marker priority when several overlap, and a cap on how many show.
- [ ] Do markers expire, or get cleared when visited (a shrine used up)?
- [ ] Phone safe area (camera hole, rounded corners) for the top corners.
- [ ] Do barrels deserve a marker at all? They have one for the prototype only.
- [ ] Should the facing wedge name the marker it points at (as the compass
      cursor did)?

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-14 | v1 | Fixed screen-relative compass bar with a facing cursor; reveal states; WorldMarkers separate from the UI. Prototype built. |
| 2026-11-14 | v2 | Switched to a screen-aligned minimap in the top-right corner; compass bar kept inert for comparison. |
| 2026-11-14 | v3 | Decided: minimap. |
