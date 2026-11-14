# Minimap and world markers

Status: v1 (prototype), 2026-11-14. Documents the minimap **as built**. Why
it is shaped this way: [design/minimap.md](../design/minimap.md).

## Overview

A round, 16-sided map in the top-right corner shows marked places within
40 m of the player. It is turned to match the screen: up on the map is up
on the screen. The player is the centre, drawn as a white wedge pointing
where the character faces. A faint ring marks half the range (20 m).

## Reveal states

| State | Shown as | Reached when |
| --- | --- | --- |
| Hidden | nothing | the start, for most kinds |
| Rumoured | grey "?" on the rim, in its direction | within `RumourRange`, or by `Reveal` |
| Sensed | dim icon at its place | within `SenseRange` |
| Discovered | full icon with outline | on screen once (`DiscoverOnScreen`), or by `Reveal` |

States only go up. Distances are flat (ground only), from the player. A
sensed or discovered marker beyond the map's range sits on the rim,
slightly smaller and dimmer.

## Prototype markers in main.tscn

| Marker | Where | Shape | Rumour / sense range | Start |
| --- | --- | --- | --- | --- |
| Shrine | (30, -35), 46 m | gold diamond | 60 / 30 m | Hidden (Rumoured at once) |
| Ruins | (-45, -45), 64 m | blue square | 50 / 25 m | Hidden |
| Ambush | (-38, 25), 45 m | red circle | none / 30 m | Rumoured |
| Barrel (both) | start barrel, (16, 14) | brown square | none / 15 m | Hidden |

Shrine, Ruins and Ambush are `PlacedMarker` nodes under `Markers`, each with
a coloured debug pillar. The barrels get theirs from `barrel.tres`, and lose
it when broken.

## Implementation notes

| Piece | Where | Role |
| --- | --- | --- |
| `WorldMarkerDefinition` | `world_markers/`, kinds in `world_markers/kinds/` | Looks (colour, shape or icon, show distance) and discovery ranges. |
| `WorldMarkers` | `world_markers/`, node `WorldMarkers` in `main.tscn` | Fixed slots; `Add(definition, position or node)`, `Reveal(id, state)`, `Remove(id)`; updates states each frame. Group `world_markers`. |
| `PlacedMarker` | `world_markers/` | Hand-placed marker; stays in the scene and removes its marker when it leaves the tree. |
| `Minimap` | `ui/`, in `player_hud.tscn` | Draws the map in `_Draw`; finds `WorldMarkers` by group (it lives outside the world's SubViewport). |
| `MarkerIcon` | `ui/` | Draws a marker's icon or shape; shared by the minimap and the compass bar. |
| `CompassBar` | `ui/`, in `player_hud.tscn` | **Inert**: hidden and disabled; kept for comparison (see the design doc). |
| `BreakableDefinition.Marker` | `props/breakables/` | Optional marker; `BreakableManager` adds it and removes it on break. |

| Value | Default | On |
| --- | --- | --- |
| `RangeMetres` | 40 m | `Minimap` |
| Size | 400 × 400 UI px (about 180 px on a 1080-wide phone) | `Minimap` node |
| `IconSize` | 48 px | `Minimap` |
| `Sides` / `InnerRing` | 16 / 0.5 | `Minimap` |
| `MaxMarkers` | 64 | `WorldMarkers` |
| `ScreenMargin` | 0.05 | `WorldMarkers` |

## Open questions

See [design/minimap.md](../design/minimap.md).

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-14 | v1 | Prototype: WorldMarkers, Minimap, PlacedMarker, three test markers, barrel marker; compass bar kept inert. |
