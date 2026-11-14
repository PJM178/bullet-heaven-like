# Pickups

Status: v1, 2026-11-14. Documents pickups **as built**. Why it is shaped
this way: [design/pickups.md](../design/pickups.md).

## Overview

Two kinds of thing lie on the ground:

- **XP orbs**: dropped by every kill, pulled in by the player's magnet
  (`XpManager`; see [player.md](player.md), Experience and levels).
- **Touch pickups**: everything else, currently the coins. Collected when
  the player walks within `PickupRadius` of one (`PickupManager`).

## Coins

| Tier | Worth | Look |
| --- | --- | --- |
| Bronze | 1 | spinning coin, copper |
| Silver | 5 | spinning coin, grey-white |
| Gold | 25 | spinning coin, gold |
| Platinum | 100 | spinning coin, pale cyan, a shine band every other spin |
| Pouch | 500 | coin pouch, still |

- Collected coins add to `PlayerStats.Coins` (signal `CoinsChanged`). The
  count lasts for the run only; nothing is saved yet.
- Enemies don't drop coins yet. The five in `main.tscn`, beside the start
  point, are placed by hand for testing.
- The sprites come from the git-ignored `local_assets/pickups/`; without them
  coins are invisible but still collectable.

## Implementation notes

| Piece | Where | Role |
| --- | --- | --- |
| `PickupDefinition` | `pickups/PickupDefinition.cs` | Resource: `Effect` (`Coins`, `Heal`), `Amount`, look (`FramesPath`, `PixelSize`, `Height`). |
| Coin definitions | `pickups/coins/coin_*.tres` | One per tier. |
| `PickupManager` | `pickups/PickupManager.cs`, node in `main.tscn` | `Drop(definition, position)`; touch check each physics step; pool of `MaxPickups` (200) sprites. When full, a drop is collected directly instead of lost. |
| `PlacedPickup` | `pickups/PlacedPickup.cs` | Editor marker; hands itself to the manager on start, then frees itself. |
| `PlayerStats.Collect` | `player/PlayerStats.cs` | Applies a pickup's effect. |

| Value | Default | On |
| --- | --- | --- |
| `PickupRadius` | 0.6 m | `PlayerStats` |
| `MagnetRadius` | 4 m | `PlayerStats` (used by `XpManager`) |
| `MaxPickups` | 200 | `PickupManager` |

Debug: the stress-test HUD shows the coin count.

## Open questions

- [ ] Which enemies drop which coins, and how often?
- [ ] A coin counter in the real HUD.

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-14 | v1 | Touch pickups: PickupDefinition, PickupManager, PlacedPickup; five coin tiers; radii moved to PlayerStats. |
