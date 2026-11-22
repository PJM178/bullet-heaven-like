# Pickups

Status: v1.2, 2026-11-22. Documents pickups **as built**. Why it is shaped
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

- Collected coins add to `PlayerStats.Coins` (signal `CoinsChanged`). On
  death they are all banked into the `Profile` and kept between runs (see
  [meta-progression.md](meta-progression.md)).
- Coins come from breakable props' loot. Enemies don't drop coins. The
  five in `main.tscn`, beside the start point, are placed by hand for testing.

## Loot

Anything that drops pickups holds a `LootTable`: a list of `LootDrop`
lines, each rolled on its own ("Chance of MinCount to MaxCount of this
pickup"). `PickupManager.DropLoot(table, centre, scatter)` rolls the table
and scatters the results; it is the only place loot is rolled. Today only
breakables use it (inline tables in their definitions). A table shared by
several sources is saved as its own `.tres`.
- The sprites come from the git-ignored `local_assets/pickups/`; without them
  coins are invisible but still collectable.

## Implementation notes

| Piece | Where | Role |
| --- | --- | --- |
| `PickupDefinition` | `pickups/PickupDefinition.cs` | Resource: `Effect` (`Coins`, `Heal`), `Amount`, look (`FramesPath`, `PixelSize`, `Height`). |
| Coin definitions | `pickups/coins/coin_*.tres` | One per tier. |
| `PickupSimulation` | `pickups/PickupSimulation.cs` | Core (plain C#): pickup positions and definitions (swap remove, a fixed slot each), `Drop` (index or `NoPickup` when full), `RollLoot` (rolls a table into a reused results array), `Step(players, pickupRadii)` filling `Collected` (definition + player) and `RemovedSlots`. |
| `PickupManager` | `pickups/PickupManager.cs`, node in `main.tscn` | Shell: `Drop(definition, position)`, `DropLoot(table, centre, scatter)` (rolled by the core, at most 64 per roll), one `AnimatedSprite3D` per slot, `MaxPickups` (200). Gives collections to `PlayerStats.Collect`. When full, a drop is collected directly instead of lost. |
| `XpSimulation` | `pickups/XpSimulation.cs` | Core: orb pop / hover / home, per orb the player it homes to (nearest within their magnet radius); `Step(delta, players, magnetRadii, xpGained)`; `Changed` lists orbs that moved or changed index. When full, XP is merged into an existing orb. |
| `XpManager` | `pickups/XpManager.cs` | Shell: draws orb i as MultiMesh copy i, rewriting only `Changed` ones; adds each step's absorbed XP to the player in one `AddExperience` call (it handles several level-ups). Separate from `PickupManager`. |
| `LootTable` / `LootDrop` | `pickups/LootTable.cs`, `pickups/LootDrop.cs` | A list of loot lines; one line: pickup, chance, count range. |
| `PlacedPickup` | `pickups/PlacedPickup.cs` | Editor marker; hands itself to the manager on start, then frees itself. |
| `PlayerStats.Collect` | `player/PlayerStats.cs` | Applies a pickup's effect. |

| Value | Default | On |
| --- | --- | --- |
| `PickupRadius` | 0.6 m | `PlayerStats` |
| `MagnetRadius` | 4 m | `PlayerStats` (used by `XpManager`) |
| `MaxPickups` | 200 | `PickupManager` |

Debug: the stress-test HUD shows the coin count.

## Open questions

- [ ] Do enemies drop coins at all? (Decided for now: coins come from
      breakables and events/chests, see meta-progression.md.)
- [ ] A coin counter in the real HUD.

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-14 | v1 | Touch pickups: PickupDefinition, PickupManager, PlacedPickup; five coin tiers; radii moved to PlayerStats. |
| 2026-11-15 | v1.1 | `LootTable` + `PickupManager.DropLoot` (loot rolled in one place); coins banked on death. |
| 2026-11-22 | v1.2 | Split into cores (`PickupSimulation`, `XpSimulation`) and shells (`PickupManager`, `XpManager`); cores take player lists; behaviour unchanged. |
